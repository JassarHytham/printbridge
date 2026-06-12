<#
.SYNOPSIS
  Creates a self-signed code-signing certificate for signing the mfilemon port
  monitor DLL, so it can load on 64-bit Windows under test-signing mode.

.NOTES
  Run once on your build machine (elevated PowerShell). Produces:
    installer/certs/PrintBridge.cer   (public  - safe to commit / distribute to your PCs)
    installer/certs/PrintBridge.pfx   (PRIVATE - DO NOT COMMIT; keep the password safe)

  Then sign the port monitor DLL:
    signtool sign /f installer\certs\PrintBridge.pfx /p <password> ^
        /fd SHA256 third_party\mfilemon\mfilemon.dll
  Verify (after the .cer is trusted on the machine, in test-signing mode):
    signtool verify /pa third_party\mfilemon\mfilemon.dll
#>

param(
    [string]$CertName = "PrintBridge Test CA",
    [string]$OutDir   = (Join-Path $PSScriptRoot "certs"),
    [Parameter(Mandatory = $true)][string]$PfxPassword
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Write-Host "Creating self-signed code-signing cert '$CertName'..."
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject "CN=$CertName" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyUsage DigitalSignature `
    -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears(5)

$cerPath = Join-Path $OutDir "PrintBridge.cer"
$pfxPath = Join-Path $OutDir "PrintBridge.pfx"

Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
$securePw = ConvertTo-SecureString -String $PfxPassword -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePw | Out-Null

Write-Host "Wrote:"
Write-Host "  $cerPath  (public)"
Write-Host "  $pfxPath  (PRIVATE - do not commit)"
Write-Host ""
Write-Host "Next: sign third_party\mfilemon\mfilemon.dll with signtool (see header)."
