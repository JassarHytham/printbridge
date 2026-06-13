<#
.SYNOPSIS
  Installs the "Shared Printer (PrintBridge)" virtual printer on this PC.
  Uses a Standard TCP/IP RAW port pointing to 127.0.0.1:9100 — no custom
  DLLs, no driver signing, no test-signing mode. Works on x64 and ARM64.

.DESCRIPTION
  Must run elevated (Administrator). Designed to be invoked by the app on
  first "Use selected printer", or by the Inno Setup installer.
#>

param(
    [string]$InstallRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = "Stop"

function Assert-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p  = New-Object Security.Principal.WindowsPrincipal($id)
    if (-not $p.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
        throw "This script must be run as Administrator."
    }
}

Assert-Admin

$printerName = "Shared Printer (PrintBridge)"
$portName    = "PrintBridge_RAW"
$portAddress = "127.0.0.1"
$portNumber  = 9100

Write-Host "Setting up '$printerName'..."

# --- Remove existing printer/port if present (idempotent re-run) ---------------
if (Get-Printer -Name $printerName -ErrorAction SilentlyContinue) {
    Write-Host "Removing existing printer..."
    Remove-Printer -Name $printerName -ErrorAction SilentlyContinue
}
if (Get-PrinterPort -Name $portName -ErrorAction SilentlyContinue) {
    Write-Host "Removing existing port..."
    Remove-PrinterPort -Name $portName -ErrorAction SilentlyContinue
}

# --- Create a Standard TCP/IP RAW port to 127.0.0.1:9100 ----------------------
# Uses Windows' built-in TCPMON.DLL — no extra DLLs required.
Write-Host "Creating RAW port '$portName' -> $portAddress`:$portNumber ..."

try {
    # Modern cmdlet: Windows 8+ / Server 2012+
    Add-PrinterPort -Name $portName -PrinterHostAddress $portAddress -PortNumber $portNumber
} catch {
    # WMI fallback for Windows 7
    Write-Host "Add-PrinterPort failed ($($_.Exception.Message)), trying WMI fallback..."
    $wmiPort = ([wmiclass]"Win32_TCPIPPrinterPort").CreateInstance()
    $wmiPort.Name        = $portName
    $wmiPort.HostAddress = $portAddress
    $wmiPort.PortNumber  = $portNumber
    $wmiPort.Protocol    = 1   # 1 = RAW
    $wmiPort.Queue       = "RAW"
    $wmiPort.Put() | Out-Null
}

# --- Find an in-box PostScript driver ------------------------------------------
$psDriverCandidates = @(
    "Microsoft PS Class Driver",       # Windows 10 / 11 (x64, ARM64, x86)
    "MS Publisher Imagesetter",         # Windows 7 / 8 in-box PS driver
    "HP Color LaserJet 2800 Series PS"  # widely distributed PS driver
)

$driverName = $null
foreach ($cand in $psDriverCandidates) {
    # Already installed?
    if (Get-PrinterDriver -Name $cand -ErrorAction SilentlyContinue) {
        $driverName = $cand
        break
    }
    # Try to install from the Windows built-in driver store (works on x64 + ARM64).
    try {
        Add-PrinterDriver -Name $cand -ErrorAction Stop
        $driverName = $cand
        break
    } catch { }
}

if (-not $driverName) {
    Write-Warning "No PostScript driver found automatically."
    Write-Warning "To get one: add any PostScript printer in Windows once, then remove it (the driver stays)."
    Write-Warning "Then re-run this script. List drivers with: Get-PrinterDriver | Select Name"
    throw "PostScript driver not found - see warnings above."
}

Write-Host "Using driver: $driverName"

# --- Create the virtual printer ------------------------------------------------
Add-Printer -Name $printerName -DriverName $driverName -PortName $portName

Write-Host ""
Write-Host "DONE. '$printerName' is ready in Devices and Printers."
Write-Host "Make sure PrintBridge.App.exe is running before you print to it."
