using System.IO;
using System.Text.Json;
using SOG_DevOpsLastActivities.Models;

namespace SOG_DevOpsLastActivities.Services;

public sealed record ConfigLoadResult(IReadOnlyList<DevOpsConfig> Configs, IReadOnlyList<string> Errors);

/// <summary>
/// The configuration files: one JSON file per configuration in the user's roaming AppData. A file
/// dropped in the folder by hand is picked up on the next refresh like one saved from the tool.
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public ConfigStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SOG_DevOpsLastActivities",
            "Configs")) { }

    public ConfigStore(string folder) => Folder = folder;

    public string Folder { get; }

    /// <summary>
    /// Reads every configuration in the folder. A file that can't be read is reported rather than
    /// failing the lot: one hand-edited typo shouldn't hide the other configurations.
    /// </summary>
    public ConfigLoadResult LoadAll()
    {
        Directory.CreateDirectory(Folder);

        var configs = new List<DevOpsConfig>();
        var errors = new List<string>();

        foreach (var path in Directory.EnumerateFiles(Folder, "*.json"))
        {
            try
            {
                var config = JsonSerializer.Deserialize<DevOpsConfig>(File.ReadAllText(path), JsonOptions)
                             ?? throw new InvalidDataException("The file holds no configuration.");
                config.FilePath = path;
                configs.Add(config);
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        configs.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
        return new ConfigLoadResult(configs, errors);
    }

    /// <summary>
    /// Writes the configuration to a file named after it. A renamed configuration moves to its new
    /// file name, and a name already taken by another file gets a numbered one instead.
    /// </summary>
    public void Save(DevOpsConfig config)
    {
        Directory.CreateDirectory(Folder);

        var target = Path.Combine(Folder, FileNameFor(config.Name) + ".json");
        if (!SamePath(target, config.FilePath))
            target = FirstFreePath(target);

        File.WriteAllText(target, JsonSerializer.Serialize(config, JsonOptions));

        if (config.FilePath.Length > 0 && !SamePath(target, config.FilePath) && File.Exists(config.FilePath))
            File.Delete(config.FilePath);

        config.FilePath = target;
    }

    public void Delete(DevOpsConfig config)
    {
        if (File.Exists(config.FilePath)) File.Delete(config.FilePath);
    }

    private static string FileNameFor(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Length == 0 ? "config" : cleaned;
    }

    private static string FirstFreePath(string path)
    {
        if (!File.Exists(path)) return path;

        var folder = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({n}).json");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static bool SamePath(string a, string b) =>
        b.Length > 0 && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
