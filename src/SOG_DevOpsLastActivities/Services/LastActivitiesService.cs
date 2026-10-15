using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using SOG_DevOpsLastActivities.Models;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace SOG_DevOpsLastActivities.Services;

/// <summary>Where a scan is. <see cref="Total"/> is 0 while the amount of work isn't known yet.</summary>
public sealed record ScanProgress(string Message, int Done = 0, int Total = 0);

public sealed record ScanResult(IReadOnlyList<WorkItemActivity> Items, IReadOnlyList<string> Warnings, int ReadCount, TimeSpan Elapsed);

/// <summary>
/// Finds the work items the PAT's owner did something on between two dates, and what they did.
/// <para>
/// Azure DevOps has no "my history" call, so this works in two passes. First it gathers candidate
/// work items from everything that knows about the user: who ever changed an item (WIQL), the
/// "My activity" list (viewed, edited, deleted or restored in about 30 days), and the
/// @RecentMentions and @Follows macros. Then it reads each candidate's history and comments and
/// keeps what the user did inside the range. The candidates only decide what gets read; the
/// history decides what gets reported.
/// </para>
/// </summary>
public sealed class LastActivitiesService(DevOpsClient client)
{
    /// <summary>About how far back the "My activity" list goes.</summary>
    public const int RecentActivityDays = 30;

    /// <summary>
    /// Histories read at once. Azure DevOps throttles on load rather than request count, and a
    /// handful in parallel brings a two-week scan down to seconds.
    /// </summary>
    private const int MaxParallelReads = 6;

    private static readonly string[] DetailFields =
        ["System.Id", "System.WorkItemType", "System.Title", "System.State", "System.AssignedTo", "System.Parent", "System.ChangedDate"];

    private static readonly string[] ParentFields = ["System.Id", "System.WorkItemType", "System.Title"];

    public async Task<ScanResult> ScanAsync(Identity me, DateTime from, DateTime to, IProgress<ScanProgress> progress, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var range = DateRange.FromLocalDates(from, to);
        var warnings = new List<string>();

        if (from.Date < DateTime.Today.AddDays(-RecentActivityDays))
        {
            warnings.Add($"The range starts more than {RecentActivityDays} days ago. Azure DevOps keeps viewed, deleted and restored "
                         + "work items for about that long, and likes are dated from them, so those can be missing. "
                         + "Changes and comments are complete.");
        }

        // ------------------------------------------------------------- candidates

        progress.Report(new ScanProgress("Finding work items…"));

        // WIQL compares dates by whole days, as the server sees them: a day of margin here, the
        // history pass below is exact.
        var since = from.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        const string InProject = "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project";

        // No upper bound: an edit by someone else after the range moves ChangedDate past it.
        var changedByMe = (await client.QueryIdsAsync(
            $"{InProject} AND [System.ChangedDate] >= '{since}' AND [System.ChangedBy] EVER @Me", ct).ConfigureAwait(false)).ToHashSet();

        // A mention is a comment, and a comment changes the work item: the date filter holds.
        var mentioned = (await TryQueryAsync(
            "@RecentMentions", $"{InProject} AND [System.ChangedDate] >= '{since}' AND [System.Id] IN (@RecentMentions)", warnings, ct).ConfigureAwait(false)).ToHashSet();

        var followed = (await TryQueryAsync(
            "@Follows", $"{InProject} AND [System.Id] IN (@Follows)", warnings, ct).ConfigureAwait(false)).ToHashSet();

        // The list holds the latest activity per work item only. Every one of them helps date
        // reactions; the ones from the range on also make their item a candidate. An item whose
        // latest activity is older than the range has had nothing done on it by the user since.
        var lastActivity = (await TryGetRecentActivityAsync(warnings, ct).ConfigureAwait(false))
            .Where(a => string.Equals(a.TeamProject, client.Project, StringComparison.OrdinalIgnoreCase))
            .GroupBy(a => a.Id)
            .ToDictionary(g => g.Key, g => g.MaxBy(a => a.ActivityDate)!);
        var recentInRange = lastActivity.Values.Where(a => a.ActivityDate >= range.Start).Select(a => a.Id).ToHashSet();

        // ---------------------------------------------------------------- details

        var allIds = changedByMe.Concat(mentioned).Concat(followed).Concat(recentInRange).Distinct().ToList();
        progress.Report(new ScanProgress($"Reading {allIds.Count} work items…"));
        var details = (await client.GetWorkItemsAsync(allIds, DetailFields, ct).ConfigureAwait(false)).ToDictionary(w => w.Id);

        // Following says nothing about when. A followed item is only worth reading if it changed
        // since the range started: a comment posted on it then is one the user may have liked.
        var followedSince = range.Start.AddDays(-1);
        var toRead = allIds
            .Where(id => details.ContainsKey(id)
                         && (changedByMe.Contains(id) || mentioned.Contains(id) || recentInRange.Contains(id)
                             || details[id].Fields.GetDate("System.ChangedDate") >= followedSince))
            .ToList();

        // ---------------------------------------------------------------- history

        var items = new ConcurrentBag<WorkItemActivity>();
        var done = 0;
        Exception? fatal = null;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var gate = new SemaphoreSlim(MaxParallelReads);

        progress.Report(new ScanProgress($"Reading histories 0/{toRead.Count}", 0, toRead.Count));

        async Task ReadOneAsync(int id)
        {
            await gate.WaitAsync(stop.Token).ConfigureAwait(false);
            try
            {
                var entries = await ReadEntriesAsync(id, me, range, lastActivity.GetValueOrDefault(id), warnings, stop.Token).ConfigureAwait(false);
                if (entries.Count > 0) items.Add(Describe(details[id], entries, me, followed.Contains(id)));
            }
            catch (DevOpsApiException ex) when (!ex.IsAuthenticationFailure)
            {
                AddWarning(warnings, $"#{id} was skipped: {ex.Message}");
            }
            catch (Exception ex) when (ex is DevOpsApiException or HttpRequestException)
            {
                // A refused PAT or a lost connection fails every other read the same way.
                Interlocked.CompareExchange(ref fatal, ex, null);
                stop.Cancel();
                throw;
            }
            finally
            {
                gate.Release();
                var n = Interlocked.Increment(ref done);
                progress.Report(new ScanProgress($"Reading histories {n}/{toRead.Count}", n, toRead.Count));
            }
        }

        try
        {
            await Task.WhenAll(toRead.Select(ReadOneAsync)).ConfigureAwait(false);
        }
        catch when (fatal is not null)
        {
            // The other reads end cancelled; the failure that stopped them is the one to report.
            ExceptionDispatchInfo.Capture(fatal).Throw();
        }

        // A deleted work item can't be read any more; the "My activity" line is all there is.
        foreach (var id in recentInRange.Where(id => !details.ContainsKey(id)))
        {
            var activity = lastActivity[id];
            if (string.Equals(activity.ActivityType, "deleted", StringComparison.OrdinalIgnoreCase) && range.Contains(activity.ActivityDate))
                items.Add(DeletedItem(activity, me));
        }

        await AddParentsAsync(items, details, warnings, ct).ConfigureAwait(false);

        var sorted = items.OrderByDescending(i => i.Entries.Max(e => e.Date)).ToList();
        return new ScanResult(sorted, warnings, toRead.Count, stopwatch.Elapsed);
    }

    private async Task<List<ActivityEntry>> ReadEntriesAsync(
        int id, Identity me, DateRange range, RecentActivityDto? recent, List<string> warnings, CancellationToken ct)
    {
        var entries = new List<ActivityEntry>();
        var historyComments = new List<ActivityEntry>();

        foreach (var update in await client.GetUpdatesAsync(id, ct).ConfigureAwait(false))
        {
            if (!me.Matches(update.RevisedBy)) continue;
            if (UpdateReader.ChangedDate(update) is not { } when || !range.Contains(when)) continue;
            entries.AddRange(UpdateReader.Read(update, when, historyComments));
        }

        IReadOnlyList<CommentDto>? comments = null;
        try
        {
            comments = await client.GetCommentsAsync(id, ct).ConfigureAwait(false);
        }
        catch (DevOpsApiException ex) when (!ex.IsAuthenticationFailure)
        {
            AddWarning(warnings, $"#{id}: its comments couldn't be read ({ex.Message}). "
                                 + "They come from its history instead, and likes on it are missing.");
        }

        // The comments themselves are the better source: the history has the comments added, but
        // not their edits and not the reactions.
        entries.AddRange(comments is null ? historyComments : CommentReader.Read(comments, me, range, recent?.ActivityDate));

        if (recent is not null && range.Contains(recent.ActivityDate))
        {
            if (string.Equals(recent.ActivityType, "visited", StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new ActivityEntry
                {
                    Kind = ActivityKind.Viewed,
                    Date = recent.ActivityDate,
                    Detail = "Opened it (only the latest visit is recorded)",
                });
            }
            else if (string.Equals(recent.ActivityType, "restored", StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new ActivityEntry { Kind = ActivityKind.Restored, Date = recent.ActivityDate, Detail = "Restored from the recycle bin" });
            }
        }

        entries.Sort((a, b) => a.Date.CompareTo(b.Date));
        return entries;
    }

    private async Task<IReadOnlyList<int>> TryQueryAsync(string macro, string wiql, List<string> warnings, CancellationToken ct)
    {
        try
        {
            return await client.QueryIdsAsync(wiql, ct).ConfigureAwait(false);
        }
        catch (DevOpsApiException ex) when (!ex.IsAuthenticationFailure)
        {
            warnings.Add($"{macro} couldn't be used through the API, so the work items it would add are left out: {ex.Message}");
            return [];
        }
    }

    private async Task<IReadOnlyList<RecentActivityDto>> TryGetRecentActivityAsync(List<string> warnings, CancellationToken ct)
    {
        try
        {
            return await client.GetMyRecentActivityAsync(ct).ConfigureAwait(false);
        }
        catch (DevOpsApiException ex) when (!ex.IsAuthenticationFailure)
        {
            warnings.Add("The \"My activity\" list couldn't be read, so viewed, deleted and restored work items are missing, "
                         + $"and likes are only kept on comments posted in the range: {ex.Message}");
            return [];
        }
    }

    private WorkItemActivity Describe(WorkItemDto item, List<ActivityEntry> entries, Identity me, bool isFollowing) => new()
    {
        Id = item.Id,
        Type = item.Fields.GetText("System.WorkItemType") ?? "",
        Title = item.Fields.GetText("System.Title") ?? "",
        State = item.Fields.GetText("System.State") ?? "",
        AssignedTo = item.Fields.GetText("System.AssignedTo"),
        IsAssignedToMe = item.Fields.TryGetValue("System.AssignedTo", out var assignee) && me.Matches(FieldValues.Identity(assignee)),
        ParentId = item.Fields.GetInt("System.Parent"),
        IsFollowing = isFollowing,
        Url = client.WorkItemWebUrl(item.Id),
        Entries = entries,
    };

    private WorkItemActivity DeletedItem(RecentActivityDto activity, Identity me) => new()
    {
        Id = activity.Id,
        Type = activity.WorkItemType ?? "",
        Title = activity.Title ?? "",
        State = activity.State ?? "",
        AssignedTo = activity.AssignedTo?.DisplayName,
        IsAssignedToMe = me.Matches(activity.AssignedTo),
        IsDeleted = true,
        Url = client.WorkItemWebUrl(activity.Id),
        Entries = [new ActivityEntry { Kind = ActivityKind.Deleted, Date = activity.ActivityDate, Detail = "Moved it to the recycle bin" }],
    };

    private async Task AddParentsAsync(
        IReadOnlyCollection<WorkItemActivity> items, Dictionary<int, WorkItemDto> details, List<string> warnings, CancellationToken ct)
    {
        var parents = new Dictionary<int, WorkItemDto>(details);
        var missing = items.Select(i => i.ParentId).OfType<int>().Where(id => !parents.ContainsKey(id)).Distinct().ToList();

        if (missing.Count > 0)
        {
            try
            {
                foreach (var parent in await client.GetWorkItemsAsync(missing, ParentFields, ct).ConfigureAwait(false))
                    parents[parent.Id] = parent;
            }
            catch (DevOpsApiException ex) when (!ex.IsAuthenticationFailure)
            {
                warnings.Add($"Parent titles couldn't be read: {ex.Message}");
            }
        }

        foreach (var item in items)
        {
            if (item.ParentId is not { } parentId) continue;
            item.ParentText = parents.TryGetValue(parentId, out var parent)
                ? $"{parent.Fields.GetText("System.WorkItemType")} {parentId}: {parent.Fields.GetText("System.Title")}"
                : $"#{parentId}";
        }
    }

    private static void AddWarning(List<string> warnings, string warning)
    {
        lock (warnings) warnings.Add(warning);
    }
}
