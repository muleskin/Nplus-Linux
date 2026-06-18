using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NPlus.Core;

/// <summary>
/// Protects small secrets (AI API keys) at rest. On Windows the value is encrypted with
/// DPAPI in CurrentUser scope, so the ciphertext on disk can only be decrypted by the same
/// Windows user — another local account or offline copy of the file is useless. On other
/// platforms DPAPI is unavailable, so the value is stored as-is and the containing file is
/// locked to owner-only permissions by the caller (see <see cref="AppPaths.TryRestrictToOwner"/>).
///
/// The stored form is tagged so secrets written by older (plaintext) builds still load,
/// giving a transparent one-way migration to encrypted-at-rest on the next save:
///   "DPAPI:" + base64(ciphertext)   → encrypted
///   anything else                   → treated as legacy plaintext
/// </summary>
public static class SecretProtector
{
    private const string DpapiPrefix = "DPAPI:";

    // CryptProtectData flag: never show a UI prompt (fail instead).
    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    /// <summary>Returns the on-disk representation of a secret (encrypted where supported).</summary>
    public static string Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return "";
        if (OperatingSystem.IsWindows())
        {
            try
            {
                byte[] cipher = Crypt(Encoding.UTF8.GetBytes(plaintext), protect: true);
                return DpapiPrefix + Convert.ToBase64String(cipher);
            }
            catch { /* fall back to plaintext if DPAPI is unavailable */ }
        }
        return plaintext;
    }

    /// <summary>Recovers the plaintext secret from its on-disk representation.</summary>
    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        if (stored.StartsWith(DpapiPrefix, StringComparison.Ordinal))
        {
            // Encrypted by a Windows build; only that same user on Windows can read it back.
            if (!OperatingSystem.IsWindows()) return "";
            try
            {
                byte[] cipher = Convert.FromBase64String(stored.Substring(DpapiPrefix.Length));
                return Encoding.UTF8.GetString(Crypt(cipher, protect: false));
            }
            catch { return ""; }
        }
        return stored; // legacy plaintext (migrated to ciphertext on next save)
    }

    // ---- DPAPI interop (crypt32) — no extra package needed on the net10.0 TFM ----

    private static byte[] Crypt(byte[] input, bool protect)
    {
        var inBlob = new DATA_BLOB();
        var outBlob = new DATA_BLOB();
        var pinned = GCHandle.Alloc(input, GCHandleType.Pinned);
        try
        {
            inBlob.cbData = input.Length;
            inBlob.pbData = pinned.AddrOfPinnedObject();

            bool ok = protect
                ? CryptProtectData(ref inBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob);
            if (!ok) throw new InvalidOperationException("DPAPI call failed: " + Marshal.GetLastWin32Error());

            byte[] result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
            return result;
        }
        finally
        {
            pinned.Free();
            if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn, string? szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved,
        IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved,
        IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
