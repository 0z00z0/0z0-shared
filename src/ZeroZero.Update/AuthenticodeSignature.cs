namespace ZeroZero.Update;

/// <summary>What WinVerifyTrust says about a file's embedded signature: the Authenticode policy,
/// no user interface, no revocation lookup, and no provider flags at all — WTD_SAFER_FLAG, set in
/// most sample code, reports every failure as "no signature", so a file altered after signing
/// would read as unsigned rather than as tampered with (measured).</summary>
internal static class AuthenticodeSignature
{
    /// <returns>The HRESULT: zero for a valid signature under a trusted chain,
    /// <c>CERT_E_UNTRUSTEDROOT</c> for a valid signature whose chain ends in a root the machine does
    /// not trust, <c>TRUST_E_NOSIGNATURE</c> for no signature, <c>TRUST_E_BAD_DIGEST</c> for a file
    /// that no longer matches its signature, and any other code as Windows reports it.</returns>
    internal static unsafe int Check(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        fixed (char* filePath = path)
        {
            var file = new NativeMethods.WINTRUST_FILE_INFO
            {
                cbStruct = (uint)sizeof(NativeMethods.WINTRUST_FILE_INFO),
                pcwszFilePath = (IntPtr)filePath,
            };
            var data = new NativeMethods.WINTRUST_DATA
            {
                cbStruct = (uint)sizeof(NativeMethods.WINTRUST_DATA),
                dwUIChoice = NativeMethods.WTD_UI_NONE,
                fdwRevocationChecks = NativeMethods.WTD_REVOKE_NONE,
                dwUnionChoice = NativeMethods.WTD_CHOICE_FILE,
                pFile = (IntPtr)(&file),
                dwStateAction = NativeMethods.WTD_STATEACTION_VERIFY,
                dwProvFlags = 0,
            };

            int result = NativeMethods.WinVerifyTrust(NativeMethods.INVALID_HANDLE_VALUE, in NativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2, ref data);

            // The state handle is released whatever the verdict was.
            data.dwStateAction = NativeMethods.WTD_STATEACTION_CLOSE;
            NativeMethods.WinVerifyTrust(NativeMethods.INVALID_HANDLE_VALUE, in NativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2, ref data);

            return result;
        }
    }
}
