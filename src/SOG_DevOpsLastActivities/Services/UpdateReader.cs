using System.Text.Json;
using SOG_DevOpsLastActivities.Models;

namespace SOG_DevOpsLastActivities.Services;

/// <summary>Turns one revision of a work item into the things done in it.</summary>
internal static class UpdateReader
{
    /// <summary>
    /// Fields that every revision touches, or that only mirror another change (the parent field
    /// follows the parent link, the resolved date follows the state). Listing them would bury the
    /// edit that mattered.
    /// </summary>
    private static readonly HashSet<string> Bookkeeping = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.Id", "System.Rev", "System.AuthorizedDate", "System.RevisedDate", "System.ChangedDate",
        "System.ChangedBy", "System.AuthorizedAs", "System.PersonId", "System.Watermark", "System.CommentCount",
        "System.IsDeleted", "System.AreaId", "System.IterationId", "System.NodeName", "System.TeamProject",
        "System.ExternalLinkCount", "System.HyperLinkCount", "System.AttachedFileCount", "System.RelatedLinkCount",
        "System.RemoteLinkCount", "System.BoardColumnDone", "System.Reason", "System.Parent",
        "System.CreatedDate", "System.CreatedBy", "System.WorkItemType",
        "Microsoft.VSTS.Common.StateChangeDate", "Microsoft.VSTS.Common.ActivatedDate", "Microsoft.VSTS.Common.ActivatedBy",
        "Microsoft.VSTS.Common.ResolvedDate", "Microsoft.VSTS.Common.ResolvedBy", "Microsoft.VSTS.Common.ClosedDate",
        "Microsoft.VSTS.Common.ClosedBy", "Microsoft.VSTS.Common.StackRank", "Microsoft.VSTS.Common.BacklogPriority",
    };

    /// <summary>
    /// Area and iteration levels mirror their paths; the WEF_ fields are the board's own copies of
    /// the column and lane, which System.BoardColumn and System.BoardLane already report.
    /// </summary>
    private static readonly string[] BookkeepingPrefixes = ["System.AreaLevel", "System.IterationLevel", "WEF_"];

    /// <summary>
    /// When the revision was made. Not the update's revisedDate: that one is when the revision was
    /// replaced by the next, 9999-01-01 for the latest.
    /// </summary>
    public static DateTimeOffset? ChangedDate(WorkItemUpdateDto update)
    {
        if (update.Fields is null) return null;

        foreach (var name in (string[])["System.ChangedDate", "System.AuthorizedDate"])
        {
            if (update.Fields.TryGetValue(name, out var change) && FieldValues.Date(change.NewValue) is { } date)
                return date;
        }
        return null;
    }

    /// <summary>
    /// The entries for one revision. A comment found in the History field goes to
    /// <paramref name="historyComments"/> instead: the comments themselves are the better source,
    /// and these are only used when they can't be read.
    /// </summary>
    public static IEnumerable<ActivityEntry> Read(WorkItemUpdateDto update, DateTimeOffset when, List<ActivityEntry> historyComments)
    {
        var fields = update.Fields ?? [];

        if (update.Rev == 1)
        {
            var type = fields.TryGetValue("System.WorkItemType", out var typeChange) ? FieldValues.Text(typeChange.NewValue) : null;
            yield return new ActivityEntry
            {
                Kind = ActivityKind.Created,
                Date = when,
                Detail = type is null ? "Created" : $"Created this {type}",
            };
        }
        else
        {
            if (fields.TryGetValue("System.State", out var state))
            {
                var from = FieldValues.Text(state.OldValue) ?? "?";
                var to = FieldValues.Text(state.NewValue) ?? "?";
                var reason = fields.TryGetValue("System.Reason", out var reasonChange) ? FieldValues.Text(reasonChange.NewValue) : null;
                yield return new ActivityEntry
                {
                    Kind = ActivityKind.StateChanged,
                    Date = when,
                    FromState = from,
                    ToState = to,
                    Detail = reason is null || reason == to ? $"{from} → {to}" : $"{from} → {to} ({reason})",
                };
            }

            var edits = fields
                .Where(f => f.Key is not ("System.State" or "System.History") && !IsBookkeeping(f.Key))
                .Select(f => Describe(f.Key, f.Value))
                .ToList();
            if (edits.Count > 0)
                yield return new ActivityEntry { Kind = ActivityKind.Edited, Date = when, Detail = string.Join("; ", edits) };

            if (fields.TryGetValue("System.History", out var history) && history.NewValue.ValueKind == JsonValueKind.String)
            {
                historyComments.Add(new ActivityEntry
                {
                    Kind = ActivityKind.Commented,
                    Date = when,
                    Detail = FieldValues.Quote(FieldValues.Snippet(history.NewValue.GetString())),
                });
            }
        }

        foreach (var relation in update.Relations?.Added ?? []) yield return RelationEntry(relation, added: true, when);
        foreach (var relation in update.Relations?.Removed ?? []) yield return RelationEntry(relation, added: false, when);
    }

    private static bool IsBookkeeping(string field) =>
        Bookkeeping.Contains(field) || BookkeepingPrefixes.Any(p => field.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>"Assigned To: Bob → Alice" when both values are short; just the field's name for
    /// descriptions, long paths and the like.</summary>
    private static string Describe(string field, FieldUpdateDto change)
    {
        var label = field.Equals("System.BoardColumn", StringComparison.OrdinalIgnoreCase) ? "Board column" : FieldValues.FieldName(field);
        var before = ValueText(change.OldValue);
        var after = ValueText(change.NewValue);
        return before is not null && after is not null && IsShort(before) && IsShort(after)
            ? $"{label}: {before} → {after}"
            : label;
    }

    /// <summary>"(empty)" for no value, null for a value with no short form.</summary>
    private static string? ValueText(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return "(empty)";
        var text = FieldValues.Text(value);
        return text is null ? null : text.Length == 0 ? "(empty)" : text;
    }

    private static bool IsShort(string text) => text.Length <= 60 && text.IndexOfAny(['<', '\n', '\r']) < 0;

    private static ActivityEntry RelationEntry(RelationDto relation, bool added, DateTimeOffset when)
    {
        var name = relation.Attributes?.TryGetValue("name", out var nameValue) == true && nameValue.ValueKind == JsonValueKind.String
            ? nameValue.GetString()
            : null;

        return relation.Rel == "AttachedFile"
            ? new ActivityEntry { Kind = ActivityKind.Attachment, Date = when, Detail = $"{(added ? "Added" : "Removed")} {name ?? "a file"}" }
            : new ActivityEntry { Kind = ActivityKind.Link, Date = when, Detail = $"{(added ? "Linked" : "Unlinked")} {LinkText(relation, name)}" };
    }

    private static string LinkText(RelationDto relation, string? name)
    {
        var url = relation.Url ?? "";

        if (WorkItemIdFromUrl(url) is { } id)
        {
            var role = relation.Rel switch
            {
                "System.LinkTypes.Hierarchy-Forward" => "child",
                "System.LinkTypes.Hierarchy-Reverse" => "parent",
                "System.LinkTypes.Related" => "related",
                "System.LinkTypes.Dependency-Forward" => "successor",
                "System.LinkTypes.Dependency-Reverse" => "predecessor",
                "System.LinkTypes.Duplicate-Forward" => "duplicate",
                "System.LinkTypes.Duplicate-Reverse" => "duplicate of",
                _ => name?.ToLowerInvariant() ?? "work item",
            };
            return $"{role} #{id}";
        }

        // Git artifacts: vstfs:///Git/{Kind}/{project}%2F{repository}%2F{what}
        const string Git = "vstfs:///Git/";
        if (url.StartsWith(Git, StringComparison.OrdinalIgnoreCase))
        {
            var kindEnd = url.IndexOf('/', Git.Length);
            var parts = kindEnd < 0 ? [] : url[(kindEnd + 1)..].Split("%2F", 3);
            if (parts.Length == 3)
            {
                var what = Uri.UnescapeDataString(parts[2]);
                switch (url[Git.Length..kindEnd])
                {
                    case "PullRequestId": return $"pull request {what}";
                    case "Commit": return $"commit {what[..Math.Min(8, what.Length)]}";
                    case "Ref": return $"branch {(what.StartsWith("GB") ? what[2..] : what)}";
                }
            }
        }

        return relation.Rel == "Hyperlink" ? $"hyperlink {url}" : name ?? relation.Rel ?? "an artifact";
    }

    private static int? WorkItemIdFromUrl(string url)
    {
        const string Marker = "/workItems/";
        var at = url.LastIndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        return at >= 0 && int.TryParse(url.AsSpan(at + Marker.Length), out var id) ? id : null;
    }
}
