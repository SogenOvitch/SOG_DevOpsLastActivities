using SOG_DevOpsLastActivities.Services;

namespace SOG_DevOpsLastActivities.Models;

/// <summary>
/// The user a PAT belongs to. <see cref="Id"/> is the identity's TeamFoundationId, the same id that
/// work item history, comments and mentions refer to people by.
/// </summary>
public sealed record Identity(string Id, string DisplayName, string? Account)
{
    public bool Matches(string? id) => string.Equals(id, Id, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// By id, and failing that by account name: an identity reference without an id has been seen
    /// in old history entries.
    /// </summary>
    public bool Matches(IdentityRefDto? identity) =>
        identity is not null
        && (Matches(identity.Id)
            || (Account is not null && string.Equals(identity.UniqueName, Account, StringComparison.OrdinalIgnoreCase)));
}
