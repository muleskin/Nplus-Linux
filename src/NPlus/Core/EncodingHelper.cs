using System;
using System.IO;
using System.Text;

namespace NPlus.Core;

/// <summary>
/// Encoding detection and conversion, ported from the original WinForms EditorForm.
/// Supports: ANSI (Windows-1252), UTF-8 (no BOM), UTF-8 with BOM, UTF-16 LE BOM, UTF-16 BE BOM.
/// Includes BOM-less UTF-16 heuristic detection for Windows-style log files.
/// </summary>
public static class EncodingHelper
{
    public const string Ansi = "ANSI";
    public const string Utf8 = "UTF-8";
    public const string Utf8Bom = "UTF-8-BOM";
    public const string Utf16BeBom = "UTF-16 BE BOM";
    public const string Utf16LeBom = "UTF-16 LE BOM";

    public static readonly string[] Names = { Ansi, Utf8, Utf8Bom, Utf16BeBom, Utf16LeBom };

    private static Encoding? _ansiEncoding;

    /// <summary>Must be called once at startup (registers the legacy code-pages provider).</summary>
    public static void Initialize()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _ansiEncoding = Encoding.GetEncoding(1252); // Windows-1252, matches the original's Encoding.Default
        }
        catch
        {
            _ansiEncoding = Encoding.Latin1; // fallback if code pages unavailable
        }
    }

    public static Encoding AnsiEncoding => _ansiEncoding ??= Encoding.Latin1;

    public static Encoding DefaultText => new UTF8Encoding(false); // UTF-8 without BOM

    /// <summary>Detects the encoding of a file from its BOM / byte patterns / UTF-8 validity.</summary>
    public static Encoding DetectFileEncoding(string filePath)
    {
        byte[] head = new byte[8192];
        int headLen;
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            headLen = fs.Read(head, 0, head.Length);
        }

        // BOM signatures
        if (headLen >= 2 && head[0] == 0xFE && head[1] == 0xFF) return Encoding.BigEndianUnicode; // UTF-16 BE BOM
        if (headLen >= 2 && head[0] == 0xFF && head[1] == 0xFE) return Encoding.Unicode;           // UTF-16 LE BOM
        if (headLen >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF) return new UTF8Encoding(true); // UTF-8 BOM

        // BOM-less UTF-16 (e.g. many Windows log files)
        var bomless = LooksLikeBomlessUtf16(head, headLen);
        if (bomless != null) return bomless;

        // No BOM — check if content is valid UTF-8
        try
        {
            byte[] content;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                content = ms.ToArray();
            }
            var utf8NoBom = new UTF8Encoding(false, true);
            utf8NoBom.GetString(content); // Throws on invalid UTF-8
            return utf8NoBom; // UTF-8 without BOM
        }
        catch
        {
            return AnsiEncoding; // ANSI fallback
        }
    }

    /// <summary>
    /// Returns Encoding.Unicode / BigEndianUnicode if the byte pattern strongly suggests
    /// BOM-less UTF-16 (nulls cluster on one side of byte pairs, printable ASCII on the
    /// other). Returns null otherwise.
    /// </summary>
    public static Encoding? LooksLikeBomlessUtf16(byte[] buffer, int len)
    {
        if (len < 4) return null;

        int nullsAtEven = 0, nullsAtOdd = 0;
        int printableAtEven = 0, printableAtOdd = 0;
        int evenCount = 0, oddCount = 0;
        for (int i = 0; i < len; i++)
        {
            byte b = buffer[i];
            bool isPrintable = (b >= 0x20 && b <= 0x7E) || b == 0x09 || b == 0x0A || b == 0x0D;
            if ((i & 1) == 0)
            {
                evenCount++;
                if (b == 0) nullsAtEven++;
                if (isPrintable) printableAtEven++;
            }
            else
            {
                oddCount++;
                if (b == 0) nullsAtOdd++;
                if (isPrintable) printableAtOdd++;
            }
        }
        if (evenCount == 0 || oddCount == 0) return null;

        // UTF-16 LE: nulls cluster on the odd side; even side is mostly printable.
        if (nullsAtOdd >= oddCount * 0.85
            && nullsAtEven <= evenCount * 0.05
            && printableAtEven >= evenCount * 0.70)
            return Encoding.Unicode;

        // UTF-16 BE: mirror of the above.
        if (nullsAtEven >= evenCount * 0.85
            && nullsAtOdd <= oddCount * 0.05
            && printableAtOdd >= oddCount * 0.70)
            return Encoding.BigEndianUnicode;

        return null;
    }

    public static string GetEncodingName(Encoding? enc)
    {
        if (enc == null) return Utf8;

        if (enc is UTF8Encoding && enc.GetPreamble().Length > 0) return Utf8Bom;
        if (enc is UTF8Encoding) return Utf8;
        if (enc.CodePage == 1200) return Utf16LeBom; // Encoding.Unicode
        if (enc.CodePage == 1201) return Utf16BeBom; // Encoding.BigEndianUnicode
        if (enc.CodePage == AnsiEncoding.CodePage) return Ansi;

        return enc.EncodingName;
    }

    public static Encoding GetEncodingFromName(string name) => name switch
    {
        Ansi => AnsiEncoding,
        Utf8 => new UTF8Encoding(false),
        Utf8Bom => new UTF8Encoding(true),
        Utf16BeBom => Encoding.BigEndianUnicode,
        Utf16LeBom => Encoding.Unicode,
        _ => new UTF8Encoding(false),
    };
}
