using System.Text.Json;

namespace SOG_DevOpsLastActivities.Services;

// The parts of the Azure DevOps answers the tool reads. Field values in a work item's history can
// be text, numbers, dates or identities depending on the field, so they stay JsonElement.

public sealed class ListResponse<T>
{
    public int Count { get; set; }
    public List<T> Value { get; set; } = [];
}

public sealed class IdentityRefDto
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public string? UniqueName { get; set; }
}

public sealed class WiqlResultDto
{
    public List<WorkItemReferenceDto> WorkItems { get; set; } = [];
}

public sealed class WorkItemReferenceDto
{
    public int Id { get; set; }
}

public sealed class WorkItemDto
{
    public int Id { get; set; }
    public Dictionary<string, JsonElement> Fields { get; set; } = [];
}

public sealed class WorkItemUpdateDto
{
    public int Id { get; set; }
    public int Rev { get; set; }
    public IdentityRefDto? RevisedBy { get; set; }
    public Dictionary<string, FieldUpdateDto>? Fields { get; set; }
    public RelationUpdatesDto? Relations { get; set; }
}

public sealed class FieldUpdateDto
{
    /// <summary>Undefined (not null) when the field had no value before.</summary>
    public JsonElement OldValue { get; set; }
    public JsonElement NewValue { get; set; }
}

public sealed class RelationUpdatesDto
{
    public List<RelationDto>? Added { get; set; }
    public List<RelationDto>? Removed { get; set; }
}

public sealed class RelationDto
{
    public string? Rel { get; set; }
    public string? Url { get; set; }
    public Dictionary<string, JsonElement>? Attributes { get; set; }
}

public sealed class CommentListDto
{
    public List<CommentDto> Comments { get; set; } = [];
    public string? ContinuationToken { get; set; }
}

public sealed class CommentDto
{
    /// <summary>The reference documents <c>id</c>; its own examples send <c>commentId</c>.</summary>
    public int Id { get; set; }
    public int CommentId { get; set; }
    public int Version { get; set; }
    public string? Text { get; set; }
    public IdentityRefDto? CreatedBy { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
    public IdentityRefDto? ModifiedBy { get; set; }
    public DateTimeOffset ModifiedDate { get; set; }
    public bool IsDeleted { get; set; }
    public List<CommentReactionDto>? Reactions { get; set; }
    public List<CommentMentionDto>? Mentions { get; set; }
}

public sealed class CommentReactionDto
{
    public string? Type { get; set; }
    public int Count { get; set; }
    public bool IsCurrentUserEngaged { get; set; }
}

public sealed class CommentMentionDto
{
    public string? ArtifactType { get; set; }
    public string? TargetId { get; set; }
}

/// <summary>One line of the "My activity" list: the user's latest activity on a work item.</summary>
public sealed class RecentActivityDto
{
    public int Id { get; set; }
    public DateTimeOffset ActivityDate { get; set; }

    /// <summary>visited, edited, deleted or restored.</summary>
    public string? ActivityType { get; set; }

    public string? TeamProject { get; set; }
    public string? WorkItemType { get; set; }
    public string? Title { get; set; }
    public string? State { get; set; }
    public IdentityRefDto? AssignedTo { get; set; }
}
