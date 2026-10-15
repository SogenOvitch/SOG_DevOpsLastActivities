namespace SOG_DevOpsLastActivities.Models;

/// <summary>A work item and everything the user did on it in the range, oldest first.</summary>
public sealed class WorkItemActivity
{
    public required int Id { get; init; }
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required string State { get; init; }
    public string? AssignedTo { get; init; }

    /// <summary>Compared by identity, not by name: two people can share a display name.</summary>
    public bool IsAssignedToMe { get; init; }

    public int? ParentId { get; init; }

    /// <summary>"User Story 123: Title", filled in once the parents have been read.</summary>
    public string? ParentText { get; set; }

    public bool IsFollowing { get; init; }
    public bool IsDeleted { get; init; }
    public required string Url { get; init; }
    public required IReadOnlyList<ActivityEntry> Entries { get; init; }
}

/// <summary>From the start of the first day to the end of the last, in local time.</summary>
public readonly record struct DateRange(DateTimeOffset Start, DateTimeOffset End)
{
    public static DateRange FromLocalDates(DateTime from, DateTime to) => new(
        new DateTimeOffset(DateTime.SpecifyKind(from.Date, DateTimeKind.Local)),
        new DateTimeOffset(DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Local)));

    /// <summary>The end is exclusive: midnight after the last day.</summary>
    public bool Contains(DateTimeOffset moment) => moment >= Start && moment < End;
}
