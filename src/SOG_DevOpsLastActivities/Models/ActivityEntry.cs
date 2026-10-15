namespace SOG_DevOpsLastActivities.Models;

/// <summary>One thing the user did on a work item.</summary>
public sealed class ActivityEntry
{
    public required ActivityKind Kind { get; init; }

    /// <summary>
    /// When it happened. For an approximate entry, the latest moment of its window that falls in
    /// the range: what it sorts by.
    /// </summary>
    public required DateTimeOffset Date { get; init; }

    public required string Detail { get; init; }

    /// <summary>
    /// Set when Azure DevOps keeps no date for the action (a reaction): it happened from
    /// <see cref="WindowStart"/> to <see cref="WindowEnd"/> (exclusive), and when the end is null,
    /// at some point after the start.
    /// </summary>
    public DateTimeOffset? WindowStart { get; init; }

    public DateTimeOffset? WindowEnd { get; init; }

    public bool IsApproximate => WindowStart is not null;

    /// <summary>For a state change, so a run of them can be summed up as first → last.</summary>
    public string? FromState { get; init; }

    public string? ToState { get; init; }

    public string KindLabel => ActivityKinds.EntryLabel(Kind);

    /// <summary>The date as the timeline shows it.</summary>
    public string DateText
    {
        get
        {
            if (WindowStart is not { } windowStart) return $"{Date.LocalDateTime:ddd dd/MM HH:mm}";

            var first = windowStart.LocalDateTime.Date;
            if (WindowEnd is not { } windowEnd) return $"on or after {first:ddd dd/MM}";

            var last = windowEnd.AddTicks(-1).LocalDateTime.Date;
            return first == last ? $"{first:ddd dd/MM}, time unknown" : $"between {first:ddd dd/MM} and {last:ddd dd/MM}";
        }
    }

    /// <summary>The date as the work item list shows it.</summary>
    public string ShortDateText =>
        IsApproximate ? $"≈ {Date.LocalDateTime:ddd dd/MM}" : $"{Date.LocalDateTime:ddd dd/MM HH:mm}";
}
