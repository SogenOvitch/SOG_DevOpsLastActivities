using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SOG_DevOpsLastActivities.Services;

/// <summary>Work item field values and texts as a person would write them.</summary>
internal static partial class FieldValues
{
    public static string? GetText(this Dictionary<string, JsonElement> fields, string name) =>
        fields.TryGetValue(name, out var value) ? Text(value) : null;

    public static int? GetInt(this Dictionary<string, JsonElement> fields, string name) =>
        fields.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    public static DateTimeOffset? GetDate(this Dictionary<string, JsonElement> fields, string name) =>
        fields.TryGetValue(name, out var value) ? Date(value) : null;

    /// <summary>An identity field's value (Assigned To and the like); null when empty.</summary>
    public static IdentityRefDto? Identity(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;

        string? Property(string name) =>
            value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

        return new IdentityRefDto { Id = Property("id"), DisplayName = Property("displayName"), UniqueName = Property("uniqueName") };
    }

    public static DateTimeOffset? Date(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;

    /// <summary>
    /// Text, numbers, yes/no and identities; null for no value and for shapes with no short form.
    /// Identities come as objects in current history and as "Name &lt;DOMAIN\user&gt;" in old revisions.
    /// </summary>
    public static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => Clean(value.GetString() ?? ""),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "Yes",
        JsonValueKind.False => "No",
        JsonValueKind.Object when value.TryGetProperty("displayName", out var name) => name.GetString(),
        _ => null,
    };

    private static string Clean(string text)
    {
        if (IsoDate().IsMatch(text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            var local = date.LocalDateTime;
            return local.TimeOfDay == TimeSpan.Zero ? local.ToString("d") : local.ToString("g");
        }

        var open = text.LastIndexOf('<');
        return open > 0 && text.EndsWith('>') && text.IndexOf('<') == open ? text[..open].TrimEnd() : text;
    }

    /// <summary>A comment or rich-text field reduced to one line of plain text.</summary>
    public static string Snippet(string? html, int maxLength = 140)
    {
        if (string.IsNullOrEmpty(html)) return "";

        var text = WebUtility.HtmlDecode(Tags().Replace(html, " "));
        text = Spaces().Replace(text, " ").Trim();
        return text.Length <= maxLength ? text : text[..(maxLength - 1)].TrimEnd() + "…";
    }

    public static string Quote(string text) => text.Length == 0 ? "(no text)" : $"“{text}”";

    /// <summary>"Microsoft.VSTS.Scheduling.RemainingWork" becomes "Remaining Work".</summary>
    public static string FieldName(string referenceName)
    {
        var name = referenceName[(referenceName.LastIndexOf('.') + 1)..];
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            var startsWord = i > 0 && char.IsUpper(c)
                             && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1])));
            if (startsWord) builder.Append(' ');
            builder.Append(c == '_' ? ' ' : c);
        }
        return builder.ToString();
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T")]
    private static partial Regex IsoDate();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
