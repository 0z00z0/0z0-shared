using System.Runtime.InteropServices;

namespace ZeroZero.Update;

/// <summary>The one import: WinVerifyTrust and the two structures it reads. Source-generated
/// interop, as the Win32 assembly does it, so the marshalling is checked at compile time.</summary>
internal static partial class NativeMethods
{
    internal static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    internal static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    internal const uint WTD_UI_NONE = 2;
    internal const uint WTD_REVOKE_NONE = 0;
    internal const uint WTD_CHOICE_FILE = 1;
    internal const uint WTD_STATEACTION_VERIFY = 1;
    internal const uint WTD_STATEACTION_CLOSE = 2;

    internal const int S_OK = 0;
    internal const int TRUST_E_PROVIDER_UNKNOWN = unchecked((int)0x800B0001);
    internal const int TRUST_E_SUBJECT_FORM_UNKNOWN = unchecked((int)0x800B0003);
    internal const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
    internal const int CERT_E_EXPIRED = unchecked((int)0x800B0101);
    internal const int CERT_E_UNTRUSTEDROOT = unchecked((int)0x800B0109);
    internal const int CERT_E_CHAINING = unchecked((int)0x800B010A);
    internal const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);
    internal const int CRYPT_E_FILE_ERROR = unchecked((int)0x80092003);

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [LibraryImport("wintrust.dll")]
    internal static partial int WinVerifyTrust(IntPtr window, in Guid action, ref WINTRUST_DATA data);
}
