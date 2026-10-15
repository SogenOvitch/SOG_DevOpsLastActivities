using SOG_DevOpsLastActivities.Models;

namespace SOG_DevOpsLastActivities.Services;

/// <summary>Turns a work item's comments into the user's comments, edits, mentions and reactions.</summary>
internal static class CommentReader
{
    /// <param name="lastActivity">
    /// The user's latest recorded activity on the work item, from the "My activity" list: the upper
    /// bound for when a reaction happened. Null when the list doesn't have the item.
    /// </param>
    public static IEnumerable<ActivityEntry> Read(IReadOnlyList<CommentDto> comments, Identity me, DateRange range, DateTimeOffset? lastActivity)
    {
        foreach (var comment in comments)
        {
            if (comment.IsDeleted) continue;

            var byMe = me.Matches(comment.CreatedBy);
            var text = FieldValues.Quote(FieldValues.Snippet(comment.Text));

            if (byMe && range.Contains(comment.CreatedDate))
                yield return new ActivityEntry { Kind = ActivityKind.Commented, Date = comment.CreatedDate, Detail = text };

            if (comment.Version > 1
                && me.Matches(comment.ModifiedBy)
                && comment.ModifiedDate > comment.CreatedDate
                && range.Contains(comment.ModifiedDate))
            {
                yield return new ActivityEntry { Kind = ActivityKind.CommentEdited, Date = comment.ModifiedDate, Detail = text };
            }

            if (!byMe && range.Contains(comment.CreatedDate) && MentionsMe(comment, me))
                yield return new ActivityEntry { Kind = ActivityKind.Mentioned, Date = comment.CreatedDate, Detail = $"by {Author(comment)}: {text}" };

            foreach (var reaction in comment.Reactions ?? [])
            {
                if (!reaction.IsCurrentUserEngaged) continue;
                if (ReactionWindow(comment.CreatedDate, lastActivity, range) is not { } window) continue;

                var (start, end) = window;
                var whose = byMe ? "your own comment" : $"{Author(comment)}'s comment";
                yield return new ActivityEntry
                {
                    Kind = ActivityKind.Reaction,
                    Date = end is { } e ? Min(e, range.End).AddTicks(-1) : start,
                    WindowStart = start,
                    WindowEnd = end,
                    Detail = $"{ReactionVerb(reaction.Type)} {whose}: {text}",
                };
            }
        }
    }

    /// <summary>
    /// A reaction carries no date. It came after the comment was posted; and reacting means having
    /// the work item open, which Azure DevOps records as a visit, so the user's latest recorded
    /// activity on the item bounds it from above (to the end of that day, for a page left open).
    /// Kept when that window overlaps the range. With no upper bound, only reactions to comments
    /// posted in the range are kept: anything older is more likely old than not.
    /// </summary>
    private static (DateTimeOffset Start, DateTimeOffset? End)? ReactionWindow(DateTimeOffset posted, DateTimeOffset? lastActivity, DateRange range)
    {
        if (posted >= range.End) return null;

        if (lastActivity is { } last && last >= posted)
        {
            var end = new DateTimeOffset(DateTime.SpecifyKind(last.LocalDateTime.Date.AddDays(1), DateTimeKind.Local));
            return end > range.Start ? (posted, end) : null;
        }

        return posted >= range.Start ? (posted, null) : null;
    }

    /// <summary>
    /// The mentions list when the server fills it; otherwise the user's id in the text, which is
    /// how both HTML and Markdown comments encode a mention.
    /// </summary>
    private static bool MentionsMe(CommentDto comment, Identity me) =>
        comment.Mentions?.Any(m => me.Matches(m.TargetId)) == true
        || comment.Text?.Contains(me.Id, StringComparison.OrdinalIgnoreCase) == true;

    private static string Author(CommentDto comment) => comment.CreatedBy?.DisplayName ?? "someone";

    private static string ReactionVerb(string? type) => type?.ToLowerInvariant() switch
    {
        "like" => "Liked",
        "dislike" => "Disliked",
        "heart" => "Hearted",
        "hooray" => "Cheered",
        "smile" => "Smiled at",
        "confused" => "Reacted “confused” to",
        _ => $"Reacted ({type}) to",
    };

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
