using System.Text.Json;

namespace NPlus.Core;

/// <summary>JSON pretty-print / formatting. Throws JsonException on invalid input.</summary>
public static class JsonTools
{
    private static readonly JsonSerializerOptions PrettyOptions = new() { WriteIndented = true };

    /// <summary>Re-serializes the document with indentation. Throws if the text isn't valid JSON.</summary>
    public static string Format(string text)
    {
        using var doc = JsonDocument.Parse(text);
        return JsonSerializer.Serialize(doc.RootElement, PrettyOptions);
    }

    public static bool TryParse(string text, out JsonDocument? document)
    {
        try { document = JsonDocument.Parse(text); return true; }
        catch { document = null; return false; }
    }
}
