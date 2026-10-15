namespace SOG_DevOpsLastActivities.Models;

public enum ActivityKind
{
    Created,
    StateChanged,
    Edited,
    Commented,
    CommentEdited,
    Attachment,
    Link,
    Reaction,
    Mentioned,
    Deleted,
    Restored,
    Viewed,
}

public static class ActivityKinds
{
    public static IReadOnlyList<ActivityKind> All { get; } = Enum.GetValues<ActivityKind>();

    /// <summary>
    /// Being mentioned is someone else's action, and opening a work item isn't doing anything on
    /// it; both are there to be asked for, not shown in a report of what the user did.
    /// </summary>
    public static bool IsShownByDefault(ActivityKind kind) =>
        kind is not (ActivityKind.Mentioned or ActivityKind.Viewed);

    /// <summary>The name of the filter that shows or hides this kind.</summary>
    public static string FilterLabel(ActivityKind kind) => kind switch
    {
        ActivityKind.Created => "Created",
        ActivityKind.StateChanged => "State changes",
        ActivityKind.Edited => "Edits",
        ActivityKind.Commented => "Comments",
        ActivityKind.CommentEdited => "Comment edits",
        ActivityKind.Attachment => "Attachments",
        ActivityKind.Link => "Links",
        ActivityKind.Reaction => "Likes / reactions",
        ActivityKind.Mentioned => "Mentions of me",
        ActivityKind.Deleted => "Deleted",
        ActivityKind.Restored => "Restored",
        ActivityKind.Viewed => "Viewed",
        _ => kind.ToString(),
    };

    /// <summary>The name of one entry in a work item's timeline.</summary>
    public static string EntryLabel(ActivityKind kind) => kind switch
    {
        ActivityKind.Created => "Created",
        ActivityKind.StateChanged => "State",
        ActivityKind.Edited => "Edited",
        ActivityKind.Commented => "Comment",
        ActivityKind.CommentEdited => "Comment edited",
        ActivityKind.Attachment => "Attachment",
        ActivityKind.Link => "Link",
        ActivityKind.Reaction => "Reaction",
        ActivityKind.Mentioned => "Mentioned",
        ActivityKind.Deleted => "Deleted",
        ActivityKind.Restored => "Restored",
        ActivityKind.Viewed => "Viewed",
        _ => kind.ToString(),
    };
}
