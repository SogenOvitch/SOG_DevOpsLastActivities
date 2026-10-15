using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using SOG_DevOpsLastActivities.Models;
using SOG_DevOpsLastActivities.Services;

namespace SOG_DevOpsLastActivities.ViewModels.Features;

/// <summary>
/// The work items the user did something on between two dates, for remembering what last week
/// was made of. An empty From means a week ago, an empty To means today.
/// </summary>
public sealed class LastActivitiesViewModel : FeatureViewModel
{
    private const int DefaultDays = 7;

    private readonly ObservableCollection<WorkItemActivityRow> _rows = [];
    private CancellationTokenSource? _cts;

    /// <summary>The range the current results are for, which the inputs may have moved away from since.</summary>
    private (DateTime From, DateTime To)? _resultRange;

    public LastActivitiesViewModel(SessionViewModel session) : base(session)
    {
        Rows = CollectionViewSource.GetDefaultView(_rows);
        Rows.Filter = row => row is WorkItemActivityRow { HasVisibleEntries: true } r
                             && (ShowAssignedToMe || !r.Item.IsAssignedToMe);
        Rows.SortDescriptions.Add(new SortDescription(nameof(WorkItemActivityRow.LastActivity), ListSortDirection.Descending));

        KindFilters = ActivityKinds.All
            .Select(kind => new KindFilter(kind, ActivityKinds.IsShownByDefault(kind), ApplyFilter))
            .ToList();

        RunCommand = new RelayCommand(async () => await RunAsync(), () => !IsBusy);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        OpenWorkItemCommand = new RelayCommand(p => Open(p as WorkItemActivityRow ?? SelectedRow), p => (p ?? SelectedRow) is WorkItemActivityRow);
        CopySummaryCommand = new RelayCommand(CopySummary, HasVisibleRows);
        ExportCsvCommand = new RelayCommand(ExportCsv, HasVisibleRows);
    }

    public override string Title => "Last activities";
    public override string Description => "Work items you created, changed, commented or liked between two dates";

    // -------------------------------------------------------------------- inputs

    private DateTime? _fromDate;
    public DateTime? FromDate
    {
        get => _fromDate;
        set => Set(ref _fromDate, value);
    }

    private DateTime? _toDate;
    public DateTime? ToDate
    {
        get => _toDate;
        set => Set(ref _toDate, value);
    }

    /// <summary>Shown dimmed in an empty date box: the date used when nothing is picked.</summary>
    public string DefaultFromText => $"{DateTime.Today.AddDays(-DefaultDays):d}";
    public string DefaultToText => $"{DateTime.Today:d}";

    public IReadOnlyList<KindFilter> KindFilters { get; }

    private bool _showAssignedToMe = true;

    /// <summary>
    /// Off hides the work items currently assigned to the user, leaving what they did on
    /// everyone else's: reviews, help, triage.
    /// </summary>
    public bool ShowAssignedToMe
    {
        get => _showAssignedToMe;
        set { if (Set(ref _showAssignedToMe, value)) ApplyFilter(); }
    }

    public ICommand RunCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenWorkItemCommand { get; }
    public ICommand CopySummaryCommand { get; }
    public ICommand ExportCsvCommand { get; }

    // --------------------------------------------------------------------- state

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;

            // The scan finishes on a continuation, not on an input event, and WPF only re-asks
            // commands whether they can run when something happens in the UI. Without this the
            // buttons stay greyed out until the mouse moves over them.
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private string _progressText = "";
    public string ProgressText
    {
        get => _progressText;
        private set => Set(ref _progressText, value);
    }

    private bool _isIndeterminate;
    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => Set(ref _isIndeterminate, value);
    }

    private double _progressValue;
    public double ProgressValue
    {
        get => _progressValue;
        private set => Set(ref _progressValue, value);
    }

    private double _progressMaximum = 1;
    public double ProgressMaximum
    {
        get => _progressMaximum;
        private set => Set(ref _progressMaximum, value);
    }

    private string _errorText = "";
    public string ErrorText
    {
        get => _errorText;
        private set => Set(ref _errorText, value);
    }

    private string _warningsText = "";
    public string WarningsText
    {
        get => _warningsText;
        private set => Set(ref _warningsText, value);
    }

    private string _statusText = "Pick a range, or leave it empty for the last seven days, and press F5.";
    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public ICollectionView Rows { get; }

    private WorkItemActivityRow? _selectedRow;
    public WorkItemActivityRow? SelectedRow
    {
        get => _selectedRow;
        set => Set(ref _selectedRow, value);
    }

    private IEnumerable<WorkItemActivityRow> VisibleRows => Rows.Cast<WorkItemActivityRow>();

    private bool HasVisibleRows() => !IsBusy && VisibleRows.Any();

    // ----------------------------------------------------------------------- run

    private async Task RunAsync()
    {
        var from = (FromDate ?? DateTime.Today.AddDays(-DefaultDays)).Date;
        var to = (ToDate ?? DateTime.Today).Date;
        if (from > to)
        {
            ErrorText = "From is after To.";
            return;
        }

        _cts = new CancellationTokenSource();
        IsBusy = true;
        ErrorText = "";
        WarningsText = "";
        OnPropertyChanged(nameof(DefaultFromText));
        OnPropertyChanged(nameof(DefaultToText));

        var progress = new Progress<ScanProgress>(p =>
        {
            ProgressText = p.Message;
            IsIndeterminate = p.Total == 0;
            ProgressMaximum = Math.Max(1, p.Total);
            ProgressValue = p.Done;
        });

        try
        {
            ((IProgress<ScanProgress>)progress).Report(new ScanProgress("Connecting…"));
            var me = await Session.GetCurrentUserAsync(_cts.Token);
            var result = await new LastActivitiesService(Session.Client).ScanAsync(me, from, to, progress, _cts.Token);

            _rows.Clear();
            foreach (var item in result.Items) _rows.Add(new WorkItemActivityRow(item));
            _resultRange = (from, to);
            ApplyFilter();

            WarningsText = string.Join(Environment.NewLine, result.Warnings);
            StatusText = $"{Describe(from, to)}: {StatusCounts()} · {result.ReadCount} histories read in {result.Elapsed.TotalSeconds:0.0} s";
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            StatusText = "Cancelled.";
        }
        catch (TaskCanceledException)
        {
            ErrorText = "Azure DevOps didn't answer in time.";
        }
        catch (Exception ex) when (ex is DevOpsApiException or HttpRequestException or UriFormatException)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void ApplyFilter()
    {
        var kinds = KindFilters.Where(f => f.IsChecked).Select(f => f.Kind).ToHashSet();
        foreach (var row in _rows) row.ApplyFilter(kinds);
        Rows.Refresh();

        if (_resultRange is { } range && !IsBusy)
            StatusText = $"{Describe(range.From, range.To)}: {StatusCounts()}";
    }

    private string StatusCounts()
    {
        var rows = VisibleRows.ToList();
        var actions = rows.Sum(r => r.VisibleEntries.Count);
        return $"{rows.Count} work item{(rows.Count == 1 ? "" : "s")}, {actions} action{(actions == 1 ? "" : "s")}";
    }

    private static string Describe(DateTime from, DateTime to) =>
        from == to ? $"{from:ddd d MMM}" : $"{from:ddd d MMM} → {to:ddd d MMM}";

    // ------------------------------------------------------------------- actions

    private static void Open(WorkItemActivityRow? row)
    {
        if (row is null) return;
        Process.Start(new ProcessStartInfo(row.Item.Url) { UseShellExecute = true });
    }

    /// <summary>One line per work item, ready to paste into a report or a timesheet.</summary>
    private void CopySummary()
    {
        var text = new StringBuilder();
        if (_resultRange is { } range)
            text.AppendLine($"{Describe(range.From, range.To)} ({Session.Config.Description})").AppendLine();

        foreach (var row in VisibleRows)
            text.AppendLine($"#{row.Id} {row.Type}: {row.Title} [{row.State}] — {row.Summary}");

        Clipboard.SetText(text.ToString());
        StatusText = "Summary copied to the clipboard.";
    }

    /// <summary>
    /// One line per action. The separator is the system's list separator, which is what Excel
    /// splits columns on when the file is opened by double-click (a semicolon in France).
    /// </summary>
    private void ExportCsv()
    {
        var name = _resultRange is { } range
            ? $"LastActivities_{range.From:yyyyMMdd}_{range.To:yyyyMMdd}.csv"
            : "LastActivities.csv";
        var dialog = new SaveFileDialog { FileName = name, Filter = "CSV file (*.csv)|*.csv", DefaultExt = ".csv" };
        if (dialog.ShowDialog() != true) return;

        var separator = CultureInfo.CurrentCulture.TextInfo.ListSeparator;
        string Line(params object?[] cells) => string.Join(separator, cells.Select(c => Quote(c?.ToString() ?? "")));
        static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

        var csv = new StringBuilder();
        csv.AppendLine(Line("Work item", "Type", "Title", "State", "Assigned to", "Date", "Approximate", "Action", "Detail", "Parent", "Link"));
        foreach (var row in VisibleRows)
        {
            foreach (var entry in row.VisibleEntries)
            {
                var date = entry.IsApproximate ? entry.DateText : entry.Date.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
                csv.AppendLine(Line(row.Id, row.Type, row.Title, row.State, row.AssignedTo, date, entry.IsApproximate ? "yes" : "",
                                    entry.KindLabel, entry.Detail, row.Parent, row.Item.Url));
            }
        }

        try
        {
            // With a byte order mark, or Excel reads the accents as Windows-1252.
            File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusText = $"Exported to {dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorText = $"Couldn't write the file: {ex.Message}";
        }
    }

    public override void Dispose() => _cts?.Cancel();
}
