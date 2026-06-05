using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NPlus.Core;

/// <summary>Saved macros (name → steps), persisted as indented JSON to macros.json.</summary>
public sealed class MacroStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public Dictionary<string, List<MacroStep>> Macros { get; private set; } = new();

    public void Load()
    {
        Macros.Clear();
        if (!File.Exists(AppPaths.MacrosFile)) return;
        try
        {
            string json = File.ReadAllText(AppPaths.MacrosFile);
            var data = JsonSerializer.Deserialize<Dictionary<string, List<MacroStep>>>(json);
            if (data != null) Macros = data;
        }
        catch { /* Ignore corrupt macros file */ }
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(Macros, WriteOptions);
            File.WriteAllText(AppPaths.MacrosFile, json);
        }
        catch { /* Ignore write errors */ }
    }
}
