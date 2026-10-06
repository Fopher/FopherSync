using System.Runtime.InteropServices;
using System.Text;

namespace FopherSync.Core.Security;

/// <summary>
/// Provides secure encryption and decryption of secrets (such as server and SMTP passwords)
/// using Windows Data Protection API (DPAPI via crypt32.dll).
/// Secrets are encrypted with the current Windows user's key and cannot be decrypted on other machines.
/// </summary>
public static class DpapiHelper
{
    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;
    public const string Prefix = "DPAPI:";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn,
        string szDataDescr,
        ref DATA_BLOB pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        ref DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn,
        StringBuilder? ppszDataDescr,
        ref DATA_BLOB pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        ref DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    /// <summary>
    /// Encrypts plain text using Windows DPAPI. Returns a string prefixed with "DPAPI:".
    /// </summary>
    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";
        if (plainText.StartsWith(Prefix)) return plainText; // Already encrypted

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var inBlob = new DATA_BLOB();
        var outBlob = new DATA_BLOB();
        var emptyBlob = new DATA_BLOB();

        var inHandle = GCHandle.Alloc(plainBytes, GCHandleType.Pinned);
        try
        {
            inBlob.pbData = inHandle.AddrOfPinnedObject();
            inBlob.cbData = plainBytes.Length;

            if (CryptProtectData(ref inBlob, "FopherSyncSecret", ref emptyBlob, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob))
            {
                try
                {
                    var cipherBytes = new byte[outBlob.cbData];
                    Marshal.Copy(outBlob.pbData, cipherBytes, 0, outBlob.cbData);
                    return Prefix + Convert.ToBase64String(cipherBytes);
                }
                finally
                {
                    if (outBlob.pbData != IntPtr.Zero)
                    {
                        LocalFree(outBlob.pbData);
                    }
                }
            }
        }
        catch
        {
            // If encryption fails for any reason, fallback gracefully
        }
        finally
        {
            inHandle.Free();
        }

        return plainText;
    }

    /// <summary>
    /// Decrypts a DPAPI-encrypted string prefixed with "DPAPI:".
    /// If the string is not encrypted (e.g. legacy plain text), returns it unchanged.
    /// </summary>
    public static string Unprotect(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return "";
        if (!cipherText.StartsWith(Prefix)) return cipherText; // Plain text or legacy format

        var base64 = cipherText.Substring(Prefix.Length);
        byte[] cipherBytes;
        try
        {
            cipherBytes = Convert.FromBase64String(base64);
        }
        catch
        {
            return cipherText;
        }

        var inBlob = new DATA_BLOB();
        var outBlob = new DATA_BLOB();
        var emptyBlob = new DATA_BLOB();

        var inHandle = GCHandle.Alloc(cipherBytes, GCHandleType.Pinned);
        try
        {
            inBlob.pbData = inHandle.AddrOfPinnedObject();
            inBlob.cbData = cipherBytes.Length;

            if (CryptUnprotectData(ref inBlob, null, ref emptyBlob, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob))
            {
                try
                {
                    var plainBytes = new byte[outBlob.cbData];
                    Marshal.Copy(outBlob.pbData, plainBytes, 0, outBlob.cbData);
                    return Encoding.UTF8.GetString(plainBytes);
                }
                finally
                {
                    if (outBlob.pbData != IntPtr.Zero)
                    {
                        LocalFree(outBlob.pbData);
                    }
                }
            }
        }
        catch
        {
            // If decryption fails, return empty
        }
        finally
        {
            inHandle.Free();
        }

        return "";
    }
}
