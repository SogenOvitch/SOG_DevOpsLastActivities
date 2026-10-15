using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using SOG_DevOpsLastActivities.Models;
using SOG_DevOpsLastActivities.Services;
using SOG_DevOpsLastActivities.ViewModels.Features;

namespace SOG_DevOpsLastActivities.ViewModels;

/// <summary>
/// One tab: a configuration, the API client for it, and the features that work through that client.
/// </summary>
public sealed class SessionViewModel : ObservableObject, IDisposable
{
    private Task<Identity>? _currentUser;

    public SessionViewModel(DevOpsConfig config)
    {
        Config = config;
        Client = new DevOpsClient(config);

        // The features every session offers. A new one is a FeatureViewModel subclass added here,
        // plus a DataTemplate in App.xaml that picks its view.
        Features = [new LastActivitiesViewModel(this)];
        _selectedFeature = Features[0];

        ConnectInBackground();
    }

    public DevOpsConfig Config { get; }
    public DevOpsClient Client { get; }

    public string Header => Config.DisplayName;
    public string ToolTip => $"{Config.Description}\n{Config.FilePath}";

    public ObservableCollection<FeatureViewModel> Features { get; }

    private FeatureViewModel _selectedFeature;
    public FeatureViewModel SelectedFeature
    {
        get => _selectedFeature;
        set => Set(ref _selectedFeature, value);
    }

    private string _connectionText = "Connecting…";
    public string ConnectionText
    {
        get => _connectionText;
        private set => Set(ref _connectionText, value);
    }

    private bool _isConnectionError;
    public bool IsConnectionError
    {
        get => _isConnectionError;
        private set => Set(ref _isConnectionError, value);
    }

    public bool IsOn(DevOpsConfig config) =>
        Config.FilePath.Length > 0 && config.FilePath.Length > 0
        && string.Equals(Path.GetFullPath(Config.FilePath), Path.GetFullPath(config.FilePath), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Who the PAT belongs to. Asked once per session, since every feature needs it and it doesn't
    /// change; asked again after a failure, so a network blip doesn't stick.
    /// </summary>
    public Task<Identity> GetCurrentUserAsync(CancellationToken ct)
    {
        if (_currentUser is null || _currentUser.IsFaulted || _currentUser.IsCanceled)
            _currentUser = LoadCurrentUserAsync();
        return _currentUser.WaitAsync(ct);
    }

    private async Task<Identity> LoadCurrentUserAsync()
    {
        ConnectionText = "Connecting…";
        IsConnectionError = false;
        try
        {
            var me = await Client.GetCurrentUserAsync(CancellationToken.None);
            ConnectionText = $"Connected as {me.DisplayName}";
            return me;
        }
        catch (Exception ex)
        {
            ConnectionText = $"Not connected: {(ex is TaskCanceledException ? "Azure DevOps didn't answer in time." : ex.Message)}";
            IsConnectionError = true;
            throw;
        }
    }

    /// <summary>Opening a tab says straight away whether the configuration works.</summary>
    private async void ConnectInBackground()
    {
        try
        {
            await GetCurrentUserAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is DevOpsApiException or HttpRequestException or TaskCanceledException or UriFormatException)
        {
            // Already shown in ConnectionText; the features report it again when they need the user.
        }
    }

    public void Dispose()
    {
        foreach (var feature in Features) feature.Dispose();
    }
}
