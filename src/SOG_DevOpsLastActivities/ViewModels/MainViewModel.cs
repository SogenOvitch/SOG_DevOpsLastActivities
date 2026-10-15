using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using SOG_DevOpsLastActivities.Models;
using SOG_DevOpsLastActivities.Services;

namespace SOG_DevOpsLastActivities.ViewModels;

/// <summary>The configurations on the left, and the sessions opened on them as tabs.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly ConfigStore _store;

    public MainViewModel(ConfigStore store)
    {
        _store = store;

        NewConfigCommand = new RelayCommand(NewConfig);
        EditConfigCommand = new RelayCommand(EditConfig, () => SelectedConfig is not null);
        DeleteConfigCommand = new RelayCommand(DeleteConfig, () => SelectedConfig is not null);
        OpenSessionCommand = new RelayCommand(() => OpenSession(SelectedConfig), () => SelectedConfig is not null);
        CloseSessionCommand = new RelayCommand(p => CloseSession(p as SessionViewModel));
        RefreshCommand = new RelayCommand(() => Reload());
        OpenFolderCommand = new RelayCommand(OpenFolder);

        Sessions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSessions));

        Reload();
    }

    /// <summary>Shows the editor and says whether it was confirmed. Set by whoever owns the window.</summary>
    public Func<ConfigEditorViewModel, bool>? ShowConfigEditor { get; set; }

    public string ConfigFolder => _store.Folder;

    public ObservableCollection<DevOpsConfig> Configs { get; } = [];

    private DevOpsConfig? _selectedConfig;
    public DevOpsConfig? SelectedConfig
    {
        get => _selectedConfig;
        set => Set(ref _selectedConfig, value);
    }

    public ObservableCollection<SessionViewModel> Sessions { get; } = [];

    private SessionViewModel? _selectedSession;
    public SessionViewModel? SelectedSession
    {
        get => _selectedSession;
        set => Set(ref _selectedSession, value);
    }

    public bool HasSessions => Sessions.Count > 0;

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public ICommand NewConfigCommand { get; }
    public ICommand EditConfigCommand { get; }
    public ICommand DeleteConfigCommand { get; }
    public ICommand OpenSessionCommand { get; }
    public ICommand CloseSessionCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenFolderCommand { get; }

    // ---------------------------------------------------------- configurations

    private void Reload(string? selectPath = null)
    {
        selectPath ??= SelectedConfig?.FilePath;

        var result = _store.LoadAll();
        Configs.Clear();
        foreach (var config in result.Configs) Configs.Add(config);

        SelectedConfig = Configs.FirstOrDefault(c => string.Equals(c.FilePath, selectPath, StringComparison.OrdinalIgnoreCase))
                         ?? Configs.FirstOrDefault();

        StatusText = result.Errors.Count == 0
            ? $"{Configs.Count} configuration{(Configs.Count == 1 ? "" : "s")} in {_store.Folder}"
            : $"Couldn't read {string.Join(" · ", result.Errors)}";
    }

    private void NewConfig() => Edit(new DevOpsConfig(), isNew: true);

    /// <summary>The editor works on a copy, so cancelling leaves the configuration as it was.</summary>
    private void EditConfig()
    {
        if (SelectedConfig is { } config) Edit(config.Clone(), isNew: false);
    }

    private void Edit(DevOpsConfig config, bool isNew)
    {
        var editor = new ConfigEditorViewModel(config, isNew, _store.Folder);
        if (ShowConfigEditor?.Invoke(editor) != true) return;

        editor.ApplyTo(config);
        try
        {
            _store.Save(config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Couldn't save {config.DisplayName}: {ex.Message}";
            return;
        }

        Reload(config.FilePath);

        if (!isNew && Sessions.Any(s => s.IsOn(config)))
            StatusText = $"Saved {config.DisplayName}. Its open session keeps the old settings until it is closed and opened again.";
    }

    private void DeleteConfig()
    {
        if (SelectedConfig is not { } config) return;

        var answer = MessageBox.Show(
            Application.Current.MainWindow!,
            $"Delete the configuration “{config.DisplayName}”?\n\nIts file is removed: {config.FilePath}",
            "Delete configuration",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            _store.Delete(config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Couldn't delete {config.DisplayName}: {ex.Message}";
            return;
        }

        Reload();
    }

    private void OpenFolder()
    {
        Directory.CreateDirectory(_store.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_store.Folder}\"") { UseShellExecute = true });
    }

    // ---------------------------------------------------------------- sessions

    /// <summary>A configuration already open in a tab brings that tab forward instead of a second one.</summary>
    private void OpenSession(DevOpsConfig? config)
    {
        if (config is null) return;

        var session = Sessions.FirstOrDefault(s => s.IsOn(config));
        if (session is null)
        {
            session = new SessionViewModel(config.Clone());
            Sessions.Add(session);
        }
        SelectedSession = session;
    }

    private void CloseSession(SessionViewModel? session)
    {
        if (session is null) return;

        session.Dispose();
        Sessions.Remove(session);
    }
}
