using System.Net.Http;
using System.Windows.Input;
using SOG_DevOpsLastActivities.Models;
using SOG_DevOpsLastActivities.Services;

namespace SOG_DevOpsLastActivities.ViewModels;

public sealed class ConfigEditorViewModel : ObservableObject
{
    public ConfigEditorViewModel(DevOpsConfig config, bool isNew, string folder)
    {
        _name = config.Name;
        _organization = config.Organization;
        _project = config.Project;
        _pat = config.Pat;

        WindowTitle = isNew ? "New configuration" : $"Edit {config.DisplayName}";
        StorageNote = $"Saved as plain text in {folder}. The PAT needs at least the Work Items (Read) scope.";
        TestCommand = new RelayCommand(async () => await TestAsync(), () => CanSave && !IsTesting);
    }

    public string WindowTitle { get; }
    public string StorageNote { get; }

    private string _name;
    public string Name
    {
        get => _name;
        set { if (Set(ref _name, value)) OnInputChanged(); }
    }

    private string _organization;
    public string Organization
    {
        get => _organization;
        set { if (Set(ref _organization, value)) OnInputChanged(); }
    }

    private string _project;
    public string Project
    {
        get => _project;
        set { if (Set(ref _project, value)) OnInputChanged(); }
    }

    private string _pat;
    public string Pat
    {
        get => _pat;
        set { if (Set(ref _pat, value)) OnInputChanged(); }
    }

    public bool CanSave =>
        !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Organization)
        && !string.IsNullOrWhiteSpace(Project) && !string.IsNullOrWhiteSpace(Pat);

    public ICommand TestCommand { get; }

    private bool _isTesting;
    public bool IsTesting
    {
        get => _isTesting;
        private set
        {
            if (!Set(ref _isTesting, value)) return;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private string _testResult = "";
    public string TestResult
    {
        get => _testResult;
        private set => Set(ref _testResult, value);
    }

    private bool _isTestOk;
    public bool IsTestOk
    {
        get => _isTestOk;
        private set => Set(ref _isTestOk, value);
    }

    public void ApplyTo(DevOpsConfig config)
    {
        config.Name = Name.Trim();
        config.Organization = Organization.Trim();
        config.Project = Project.Trim();
        config.Pat = Pat.Trim();
    }

    /// <summary>A result is about the values it was run with; once they change it says nothing.</summary>
    private void OnInputChanged()
    {
        OnPropertyChanged(nameof(CanSave));
        TestResult = "";
    }

    /// <summary>
    /// Checks the two things a session needs: that the PAT is accepted, and that it can query the
    /// project's work items (which also proves the project name right).
    /// </summary>
    private async Task TestAsync()
    {
        IsTesting = true;
        IsTestOk = false;
        TestResult = "Testing…";
        try
        {
            var probe = new DevOpsConfig();
            ApplyTo(probe);
            var client = new DevOpsClient(probe);

            var me = await client.GetCurrentUserAsync(CancellationToken.None);
            await client.QueryIdsAsync("SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project", CancellationToken.None, top: 1);

            TestResult = $"✔ Connected as {me.DisplayName}, and the project's work items can be read.";
            IsTestOk = true;
        }
        catch (Exception ex) when (ex is DevOpsApiException or HttpRequestException or TaskCanceledException or UriFormatException)
        {
            TestResult = ex is TaskCanceledException ? "Azure DevOps didn't answer in time." : ex.Message;
        }
        finally
        {
            IsTesting = false;
        }
    }
}
