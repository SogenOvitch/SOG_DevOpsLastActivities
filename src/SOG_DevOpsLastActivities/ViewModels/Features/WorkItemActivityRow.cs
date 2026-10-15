using SOG_DevOpsLastActivities.Models;

namespace SOG_DevOpsLastActivities.ViewModels.Features;

/// <summary>A work item in the results, showing only the kinds of activity the filters let through.</summary>
public sealed class WorkItemActivityRow(WorkItemActivity item) : ObservableObject
{
    public WorkItemActivity Item { get; } = item;

    public int Id => Item.Id;
    public string Type => Item.Type;
    public string Title => Item.Title;
    public string State => Item.IsDeleted ? $"{Item.State} (deleted)" : Item.State;
    public string Parent => Item.ParentText ?? "";
    public string AssignedTo => Item.AssignedTo ?? "";
    public string FollowingText => Item.IsFollowing ? "✓" : "";

    private IReadOnlyList<ActivityEntry> _visibleEntries = [];
    public IReadOnlyList<ActivityEntry> VisibleEntries
    {
        get => _visibleEntries;
        private set => Set(ref _visibleEntries, value);
    }

    public bool HasVisibleEntries => VisibleEntries.Count > 0;

    private string _summary = "";
    public string Summary
    {
        get => _summary;
        private set => Set(ref _summary, value);
    }

    private DateTimeOffset? _lastActivity;
    public DateTimeOffset? LastActivity
    {
        get => _lastActivity;
        private set => Set(ref _lastActivity, value);
    }

    private string _lastActivityText = "";
    public string LastActivityText
    {
        get => _lastActivityText;
        private set => Set(ref _lastActivityText, value);
    }

    /// <summary>Newest first: the question is usually "what did I just do", read from the top.</summary>
    public void ApplyFilter(IReadOnlySet<ActivityKind> kinds)
    {
        VisibleEntries = Item.Entries.Where(e => kinds.Contains(e.Kind)).OrderByDescending(e => e.Date).ToList();
        Summary = ActivitySummary.Describe(VisibleEntries);

        // A reaction's date is only an estimate, the end of its window: it would win over a real
        // date from the same day. It counts only on a work item with nothing else to show.
        var latest = VisibleEntries.Where(e => !e.IsApproximate).MaxBy(e => e.Date)
                     ?? VisibleEntries.MaxBy(e => e.Date);
        LastActivity = latest?.Date;
        LastActivityText = latest?.ShortDateText ?? "";

        OnPropertyChanged(nameof(HasVisibleEntries));
    }
}

/// <summary>A work item's entries in one line: "State: Active → Resolved · 2 comments · Edited".</summary>
public static class ActivitySummary
{
    public static string Describe(IReadOnlyList<ActivityEntry> entries)
    {
        var parts = new List<string>();
        foreach (var group in entries.GroupBy(e => e.Kind).OrderBy(g => g.Key))
        {
            // Oldest first whatever order the entries come in, so a run of state changes reads
            // from where it started to where it ended.
            var list = group.OrderBy(e => e.Date).ToList();
            var n = list.Count;
            parts.Add(group.Key switch
            {
                ActivityKind.Created => "Created",
                ActivityKind.StateChanged => n == 1
                    ? $"State: {list[0].FromState} → {list[0].ToState}"
                    : $"State: {list[0].FromState} → … → {list[^1].ToState}",
                ActivityKind.Edited => Counted(n, "Edited"),
                ActivityKind.Commented => n == 1 ? "1 comment" : $"{n} comments",
                ActivityKind.CommentEdited => Counted(n, "Comment edited"),
                ActivityKind.Attachment => n == 1 ? "1 attachment" : $"{n} attachments",
                ActivityKind.Link => n == 1 ? "1 link" : $"{n} links",
                ActivityKind.Reaction => Counted(n, "Reacted"),
                ActivityKind.Mentioned => Counted(n, "Mentioned"),
                ActivityKind.Deleted => "Deleted",
                ActivityKind.Restored => "Restored",
                ActivityKind.Viewed => "Viewed",
                _ => group.Key.ToString(),
            });
        }
        return string.Join(" · ", parts);
    }

    private static string Counted(int n, string what) => n == 1 ? what : $"{what} ×{n}";
}

public sealed class KindFilter(ActivityKind kind, bool isChecked, Action changed) : ObservableObject
{
    public ActivityKind Kind { get; } = kind;
    public string Label => ActivityKinds.FilterLabel(Kind);

    private bool _isChecked = isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set { if (Set(ref _isChecked, value)) changed(); }
    }
}
