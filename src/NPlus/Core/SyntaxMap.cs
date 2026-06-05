using System;
using System.Collections.Generic;
using System.IO;

namespace NPlus.Core;

/// <summary>
/// Maps file extensions to TextMate language ids (as bundled by TextMateSharp.Grammars),
/// covering every language the original Scintilla-based editor highlighted.
/// Returns null for plain text.
/// </summary>
public static class SyntaxMap
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // C-family / C#
        [".cs"] = "csharp",
        [".csx"] = "csharp",
        [".c"] = "c",
        [".h"] = "cpp",
        [".cpp"] = "cpp",
        [".cc"] = "cpp",
        [".cxx"] = "cpp",
        [".hpp"] = "cpp",
        // Java
        [".java"] = "java",
        // JavaScript / TypeScript
        [".js"] = "javascript",
        [".jsx"] = "javascript",
        [".mjs"] = "javascript",
        [".cjs"] = "javascript",
        [".ts"] = "typescript",
        [".tsx"] = "typescript",
        // Python
        [".py"] = "python",
        [".pyw"] = "python",
        // SQL
        [".sql"] = "sql",
        // Visual Basic / VBScript
        [".vb"] = "vb",
        [".bas"] = "vb",
        [".vbs"] = "vb",
        // PowerShell
        [".ps1"] = "powershell",
        [".psm1"] = "powershell",
        [".psd1"] = "powershell",
        [".ps"] = "powershell",
        // PHP
        [".php"] = "php",
        [".php3"] = "php",
        [".php4"] = "php",
        [".php5"] = "php",
        [".phtml"] = "php",
        // HTML / XML / XAML
        [".html"] = "html",
        [".htm"] = "html",
        [".xml"] = "xml",
        [".xaml"] = "xml",
        [".xsl"] = "xml",
        [".xslt"] = "xml",
        [".xsd"] = "xml",
        [".config"] = "xml",
        [".csproj"] = "xml",
        [".props"] = "xml",
        [".targets"] = "xml",
        // JSON
        [".json"] = "json",
        [".jsonc"] = "json",
        // YAML
        [".yml"] = "yaml",
        [".yaml"] = "yaml",
        // Bonus shells/scripts that TextMate supports natively
        [".sh"] = "shellscript",
        [".bash"] = "shellscript",
        [".css"] = "css",
        [".md"] = "markdown",
        [".markdown"] = "markdown",
        [".ini"] = "ini",
    };

    /// <summary>Returns the TextMate language id for a file path, or null for plain text.</summary>
    public static string? GetLanguageId(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;
        string ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext)) return null;
        return Map.TryGetValue(ext, out var id) ? id : null;
    }
}
