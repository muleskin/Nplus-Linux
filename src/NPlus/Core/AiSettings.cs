using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NPlus.Core;

/// <summary>Per-provider connection settings (key, endpoint, model/deployment, api-version).</summary>
public sealed class AiProviderConfig
{
    public string ApiKey { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApiVersion { get; set; } = "";
}

/// <summary>
/// AI assistant settings, persisted as JSON in <c>~/.config/nplus/ai.json</c> (separate from the
/// legacy line-based settings.txt so the structured/per-provider config stays clean). The whole
/// feature is opt-in: when <see cref="Enabled"/> is false nothing reaches out to any network.
/// </summary>
public sealed class AiSettings
{
    public bool Enabled { get; set; }

    /// <summary>The active provider, stored by enum name (e.g. "OpenAI", "Claude").</summary>
    public string Provider { get; set; } = "OpenAI";

    /// <summary>Per-provider config keyed by enum name; created lazily by <see cref="ConfigFor"/>.</summary>
    public Dictionary<string, AiProviderConfig> Providers { get; set; } = new();

    [JsonIgnore]
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>Returns (creating if needed) the config block for the given provider enum name.</summary>
    public AiProviderConfig ConfigFor(string providerName)
    {
        if (!Providers.TryGetValue(providerName, out var cfg))
        {
            cfg = new AiProviderConfig();
            Providers[providerName] = cfg;
        }
        return cfg;
    }

    public static AiSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.AiSettingsFile))
            {
                var json = File.ReadAllText(AppPaths.AiSettingsFile);
                var s = JsonSerializer.Deserialize<AiSettings>(json);
                if (s != null) return s;
            }
        }
        catch { /* fall through to defaults on any corruption */ }
        return new AiSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(AppPaths.AiSettingsFile, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch { /* best effort */ }
    }
}
