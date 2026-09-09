<#
.SYNOPSIS
Signs one or more files with a code-signing certificate and verifies what it signed.

.DESCRIPTION
The certificate comes from the current user's or the machine's personal store by thumbprint, or
from a PFX file whose password is read from the environment variable ZEROZERO_SIGN_PFX_PASSWORD,
so the password is never on a command line. The signature is SHA-256 with the full chain.

Unless -NoTimestamp is given, the file must come out timestamped. An authority that cannot be
reached, or that answers only a scheme this signer cannot use, leaves the signature intact and the
file untimestamped, and an untimestamped signature stops verifying the day the certificate expires.
That outcome fails here rather than shipping.

After signing, each file is read back and must carry a signature by the certificate that was
asked for, and a timestamp unless -NoTimestamp was given. The status must be Valid — or, without
-Trust, the untrusted-root status that a
self-signed certificate yields on a machine that has not been told to trust it, which is the
studio certificate on a fresh runner. -Trust installs the certificate into the current user's
Root and TrustedPublisher stores first, so the signature reads Valid; a certificate already there
is left alone. Any other outcome is a failure: the script exits non-zero and prints the reason in
the form MSBuild reports as an error, so a publish or an installer build that calls it stops.

Nothing is written when the certificate cannot be found or cannot sign.

.EXAMPLE
pwsh scripts\Sign-Executable.ps1 -Path .\publish\App.exe -Thumbprint 0123ABCD...

.EXAMPLE
$env:ZEROZERO_SIGN_PFX_PASSWORD = (secret)
pwsh scripts\Sign-Executable.ps1 -Path .\Output\App-Setup.exe -PfxPath .\signing.pfx -Trust
#>
[CmdletBinding(DefaultParameterSetName = "Store")]
param(
    [Parameter(Mandatory)][string[]]$Path,
    [Parameter(Mandatory, ParameterSetName = "Store")][string]$Thumbprint,
    [Parameter(Mandatory, ParameterSetName = "Pfx")][string]$PfxPath,
    [string]$TimestampServer = "http://timestamp.digicert.com",
    [switch]$NoTimestamp,
    [switch]$Trust
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$codeSigningOid = "1.3.6.1.5.5.7.3.3"

function Fail([string]$Code, [string]$Message) {
    # The canonical MSBuild error line, which Exec logs as an error and a person can read.
    Write-Host "Sign-Executable.ps1 : error ${Code}: $Message"
    exit 1
}

function Get-SigningCertificate {
    if ($PSCmdlet.ParameterSetName -eq "Store") {
        $wanted = ($Thumbprint -replace "\s", "").ToUpperInvariant()
        if ($wanted -notmatch '^[0-9A-F]{40}$') {
            Fail "ZZS001" "'$Thumbprint' is not a SHA-1 thumbprint (40 hex characters)."
        }
        foreach ($store in "Cert:\CurrentUser\My", "Cert:\LocalMachine\My") {
            $found = Get-ChildItem $store -ErrorAction SilentlyContinue |
                Where-Object { $_.Thumbprint -eq $wanted } | Select-Object -First 1
            if ($found) { return $found }
        }
        Fail "ZZS002" "No certificate with thumbprint $wanted in Cert:\CurrentUser\My or Cert:\LocalMachine\My."
    }

    if (-not (Test-Path -LiteralPath $PfxPath -PathType Leaf)) {
        Fail "ZZS003" "The PFX file '$PfxPath' does not exist."
    }
    $password = $env:ZEROZERO_SIGN_PFX_PASSWORD
    if ([string]::IsNullOrEmpty($password)) {
        Fail "ZZS004" "ZEROZERO_SIGN_PFX_PASSWORD is not set; the PFX password is read from that variable and never from an argument."
    }
    try {
        return Get-PfxCertificate -FilePath $PfxPath -Password (ConvertTo-SecureString $password -AsPlainText -Force)
    }
    catch {
        Fail "ZZS005" "The PFX file '$PfxPath' could not be opened: $($_.Exception.Message)"
    }
}

function Test-CanSign([System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate) {
    if (-not $Certificate.HasPrivateKey) {
        Fail "ZZS006" "Certificate $($Certificate.Thumbprint) ($($Certificate.Subject)) has no private key, so it cannot sign."
    }
    $usages = @($Certificate.Extensions |
        Where-Object { $_ -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } |
        ForEach-Object { $_.EnhancedKeyUsages } | ForEach-Object { $_.Value })
    if ($usages -notcontains $codeSigningOid) {
        Fail "ZZS007" "Certificate $($Certificate.Thumbprint) ($($Certificate.Subject)) is not a code-signing certificate: the enhanced key usage does not include $codeSigningOid."
    }
}

function Install-Trust([System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate) {
    # The public half only. A store that already holds it is left as it is.
    $public = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $Certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    foreach ($name in "Root", "TrustedPublisher") {
        $store = [System.Security.Cryptography.X509Certificates.X509Store]::new($name, "CurrentUser")
        try {
            $store.Open("ReadWrite")
            $present = $store.Certificates | Where-Object { $_.Thumbprint -eq $Certificate.Thumbprint }
            if (-not $present) {
                $store.Add($public)
                Write-Host "Trusted $($Certificate.Thumbprint) in CurrentUser\$name."
            }
        }
        finally { $store.Close() }
    }
}

$certificate = Get-SigningCertificate
Test-CanSign $certificate
if ($Trust) { Install-Trust $certificate }

foreach ($file in $Path) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        Fail "ZZS008" "'$file' does not exist, so there is nothing to sign."
    }
    $file = (Resolve-Path -LiteralPath $file).Path

    $arguments = @{
        FilePath      = $file
        Certificate   = $certificate
        HashAlgorithm = "SHA256"
        IncludeChain  = "All"
    }
    if (-not $NoTimestamp) { $arguments.TimestampServer = $TimestampServer }

    try { $null = Set-AuthenticodeSignature @arguments }
    catch { Fail "ZZS009" "Signing '$file' failed: $($_.Exception.Message)" }

    # Read back rather than trust the return value: the file on disk is what ships.
    $signature = Get-AuthenticodeSignature -LiteralPath $file
    if ($null -eq $signature.SignerCertificate) {
        Fail "ZZS010" "'$file' carries no signature after signing (status $($signature.Status): $($signature.StatusMessage))."
    }
    if ($signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
        Fail "ZZS011" "'$file' is signed by $($signature.SignerCertificate.Thumbprint), not by the requested $($certificate.Thumbprint)."
    }
    # A timestamp that never arrived leaves a signature indistinguishable from one asked to go
    # without: same status, same signer, an empty unsigned-attribute set. The countersignature is
    # the only thing that says the file will still verify once the certificate has expired.
    $timestamper = $signature.TimeStamperCertificate
    if (-not $NoTimestamp -and $null -eq $timestamper) {
        Fail "ZZS013" "'$file' is signed but carries no timestamp: $TimestampServer did not answer, or answered only in a scheme this signer cannot use. An untimestamped signature stops verifying when the certificate expires. Pass -NoTimestamp to sign without one deliberately."
    }

    $stamp = if ($null -eq $timestamper) { "no timestamp was asked for" } else { "timestamped by $($timestamper.Subject)" }
    $untrustedRoot = $signature.Status -eq "UnknownError" -and $signature.StatusMessage -match "not trusted"
    if ($signature.Status -eq "Valid") {
        Write-Host "Signed '$file' with $($certificate.Subject) ($($certificate.Thumbprint)): Valid, $stamp."
    }
    elseif ($untrustedRoot -and -not $Trust) {
        Write-Host "Signed '$file' with $($certificate.Subject) ($($certificate.Thumbprint)): $stamp, and the chain ends in a root this machine does not trust, which a self-signed certificate does until -Trust installs it. The signature itself is intact."
    }
    else {
        Fail "ZZS012" "'$file' verifies as $($signature.Status) after signing: $($signature.StatusMessage)"
    }
}

exit 0
