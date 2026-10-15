using System.IO;
using System.Text.Json.Serialization;

namespace SOG_DevOpsLastActivities.Models;

/// <summary>
/// One configuration file: which organisation and project to talk to, and the PAT to do it with.
/// The PAT is stored in plain text, as typed: the files are meant to be read and edited by hand.
/// </summary>
public sealed class DevOpsConfig
{
    public string Name { get; set; } = "";
    public string Organization { get; set; } = "";
    public string Project { get; set; } = "";
    public string Pat { get; set; } = "";

    /// <summary>Where the configuration was read from; empty for one not saved yet.</summary>
    [JsonIgnore]
    public string FilePath { get; set; } = "";

    [JsonIgnore]
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Name) ? Name.Trim()
        : FilePath.Length > 0 ? Path.GetFileNameWithoutExtension(FilePath)
        : "(unnamed)";

    [JsonIgnore]
    public string Description => $"{Organization.Trim()} / {Project.Trim()}";

    /// <summary>
    /// The organisation is normally just its name, but a full URL is taken as is, which also covers
    /// the old *.visualstudio.com addresses and an Azure DevOps Server collection.
    /// </summary>
    [JsonIgnore]
    public string BaseUrl
    {
        get
        {
            var organization = Organization.Trim().TrimEnd('/');
            return organization.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || organization.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? organization
                : "https://dev.azure.com/" + Uri.EscapeDataString(organization);
        }
    }

    public DevOpsConfig Clone() => (DevOpsConfig)MemberwiseClone();
}
