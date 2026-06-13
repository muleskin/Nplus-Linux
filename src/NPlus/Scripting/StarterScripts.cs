using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace NPlus.Scripting;

/// <summary>
/// Seeds the bundled example scripts (embedded under <c>Scripts/*.lua</c>) into the
/// per-user scripts folder so the Tools ▸ Scripting menu has runnable content out of
/// the box. Existing files are never overwritten — the user's edits win.
/// </summary>
public static class StarterScripts
{
    private const string ResourcePrefix = "NPlus.Scripts.";

    public static void EnsureSeeded(string scriptsDir)
    {
        try
        {
            Directory.CreateDirectory(scriptsDir);
            var asm = Assembly.GetExecutingAssembly();
            foreach (var resource in asm.GetManifestResourceNames())
            {
                if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) ||
                    !resource.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                    continue;

                string fileName = resource.Substring(ResourcePrefix.Length);
                string target = Path.Combine(scriptsDir, fileName);
                if (File.Exists(target)) continue;

                using var stream = asm.GetManifestResourceStream(resource);
                if (stream == null) continue;
                using var reader = new StreamReader(stream);
                File.WriteAllText(target, reader.ReadToEnd());
            }
        }
        catch { /* seeding is best-effort — a missing example never blocks startup */ }
    }
}
