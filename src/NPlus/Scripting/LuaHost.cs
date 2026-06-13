using System;
using System.Collections.Generic;
using System.Text;
using MoonSharp.Interpreter;

namespace NPlus.Scripting;

/// <summary>Outcome of running a Lua script — captured <c>print</c> output and any error.</summary>
public sealed class ScriptResult
{
    public bool Success { get; init; }
    public string Output { get; init; } = string.Empty;
    public string? Error { get; init; }
}

/// <summary>
/// Runs Lua scripts against the active editor through a small <c>editor</c> API.
/// Built on MoonSharp (a managed Lua interpreter) so it adds no native dependency
/// and survives the self-contained single-file publish on every RID.
///
/// Scripts run in a soft sandbox: the standard string/table/math/os(time) libraries
/// are available, but file I/O, <c>os.execute</c> and <c>loadfile</c>/<c>dofile</c> are not —
/// a script can transform the buffer but can't reach out to the filesystem or shell.
/// </summary>
public sealed class LuaHost
{
    public ScriptResult Run(string code, ScriptContext ctx)
    {
        var output = new StringBuilder();
        var script = new Script(CoreModules.Preset_SoftSandbox);
        script.Options.DebugPrint = s => output.Append(s).Append('\n');
        script.Globals["editor"] = BuildEditorApi(script, ctx);

        try
        {
            var ret = script.DoString(code, codeFriendlyName: ctx.Name);
            if (ret != null && ret.Type != DataType.Void && ret.Type != DataType.Nil)
                output.Append(ret.ToPrintString()).Append('\n');
            return new ScriptResult { Success = true, Output = output.ToString() };
        }
        catch (InterpreterException ex)
        {
            // DecoratedMessage carries the script name + line number; fall back to Message.
            return new ScriptResult { Success = false, Output = output.ToString(), Error = ex.DecoratedMessage ?? ex.Message };
        }
        catch (Exception ex)
        {
            return new ScriptResult { Success = false, Output = output.ToString(), Error = ex.Message };
        }
    }

    private static Table BuildEditorApi(Script script, ScriptContext ctx)
    {
        var api = new Table(script);

        DynValue Str(string s) => DynValue.NewString(s);
        string Arg(CallbackArguments args, int i) => args[i].CastToString() ?? string.Empty;

        api["text"] = DynValue.NewCallback((_, _) => Str(ctx.GetText()));

        api["set_text"] = DynValue.NewCallback((_, a) => { ctx.SetText(Arg(a, 0)); return DynValue.Nil; });

        api["selection"] = DynValue.NewCallback((_, _) => Str(ctx.GetSelection()));

        // Replace the current selection, or insert at the caret when nothing is selected.
        api["replace_selection"] = DynValue.NewCallback((_, a) => { ctx.ReplaceSelection(Arg(a, 0)); return DynValue.Nil; });

        api["insert"] = DynValue.NewCallback((_, a) => { ctx.Insert(Arg(a, 0)); return DynValue.Nil; });

        api["line_count"] = DynValue.NewCallback((_, _) => DynValue.NewNumber(ctx.LineCount()));

        api["caret_line"] = DynValue.NewCallback((_, _) => DynValue.NewNumber(ctx.CaretLine()));

        api["file_path"] = DynValue.NewCallback((_, _) =>
        {
            var p = ctx.FilePath();
            return p == null ? DynValue.Nil : Str(p);
        });

        api["language"] = DynValue.NewCallback((_, _) =>
        {
            var l = ctx.Language();
            return l == null ? DynValue.Nil : Str(l);
        });

        // editor.lines() -> 1-based array table of the document's lines.
        api["lines"] = DynValue.NewCallback((_, _) =>
        {
            var lines = SplitLines(ctx.GetText());
            var t = new Table(script);
            for (int i = 0; i < lines.Count; i++) t[i + 1] = lines[i];
            return DynValue.NewTable(t);
        });

        // editor.set_lines(t) -> replace the whole document with the joined array table.
        api["set_lines"] = DynValue.NewCallback((_, a) =>
        {
            string nl = DetectNewline(ctx.GetText());
            var sb = new StringBuilder();
            if (a[0].Type == DataType.Table)
            {
                var t = a[0].Table;
                int n = (int)t.Length;
                for (int i = 1; i <= n; i++)
                {
                    if (i > 1) sb.Append(nl);
                    sb.Append(t.Get(i).CastToString() ?? string.Empty);
                }
            }
            ctx.SetText(sb.ToString());
            return DynValue.Nil;
        });

        return api;
    }

    private static List<string> SplitLines(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return new List<string>(normalized.Split('\n'));
    }

    private static string DetectNewline(string text)
    {
        int i = text.IndexOf('\n');
        if (i > 0 && text[i - 1] == '\r') return "\r\n";
        return "\n";
    }
}
