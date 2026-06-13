using System;

namespace NPlus.Scripting;

/// <summary>
/// A thin, UI-free bridge between a running Lua script and the active editor tab.
/// The <see cref="LuaHost"/> exposes these delegates to scripts as the global
/// <c>editor</c> table; the view layer fills them in from the focused document so
/// the scripting engine never has to reference Avalonia/AvaloniaEdit types directly.
/// </summary>
public sealed class ScriptContext
{
    /// <summary>Friendly name used in error messages (usually the tab/file name).</summary>
    public string Name { get; init; } = "script";

    public Func<string> GetText { get; init; } = () => string.Empty;
    public Action<string> SetText { get; init; } = _ => { };
    public Func<string> GetSelection { get; init; } = () => string.Empty;
    public Action<string> ReplaceSelection { get; init; } = _ => { };
    public Action<string> Insert { get; init; } = _ => { };
    public Func<int> LineCount { get; init; } = () => 0;
    public Func<int> CaretLine { get; init; } = () => 1;
    public Func<string?> FilePath { get; init; } = () => null;
    public Func<string?> Language { get; init; } = () => null;
}
