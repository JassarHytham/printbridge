<#
.SYNOPSIS
  Installs the "Shared Printer (PrintBridge)" virtual printer on this PC:
  registers the mfilemon redirection port monitor, creates a port that pipes each
  spooled job to "PrintBridge.App.exe --capture", and adds a PostScript printer
  bound to that port.

.DESCRIPTION
  Must run elevated (Administrator). Designed to be invoked by the app on first
  "Use", or by the Inno Setup installer.

  >>> VERIFY-ON-WINDOWS markers below flag values that depend on the exact
  >>> mfilemon build and the Windows version's in-box PostScript driver name.
  >>> Adjust them on your real machines (Phase 4 Task 4.1 / Phase 6 Task 6.3).
#>

param(
    # Folder containing PrintBridge.App.exe + third_party + installer (defaults to
    # the install root: the parent of this script's folder).
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

# --- Resolve paths -----------------------------------------------------------
$appExe    = Join-Path $InstallRoot "PrintBridge.App.exe"
$certPath  = Join-Path $InstallRoot "installer\certs\PrintBridge.cer"
$is64      = [Environment]::Is64BitOperatingSystem
$mfDllSrc  = if ($is64) { Join-Path $InstallRoot "third_party\mfilemon\x64\mfilemon.dll" }
                   else { Join-Path $InstallRoot "third_party\mfilemon\x86\mfilemon.dll" }

$monitorName = "PrintBridge File Port Monitor"   # the friendly monitor name we register
$portName    = "PrintBridge:"
$printerName = "Shared Printer (PrintBridge)"

# Candidate in-box PostScript driver names, newest first.
# >>> VERIFY-ON-WINDOWS: the available name differs by OS version.
$psDriverCandidates = @(
    "Microsoft PS Class Driver",     # Windows 10 / 11
    "Microsoft XPS Document Writer v4",
    "MS Publisher Imagesetter",      # in-box PS driver present on Win7/8
    "Generic / Text Only"            # last-resort fallback (NOT PostScript - replace!)
)

Write-Host "Install root: $InstallRoot"
if (-not (Test-Path $appExe)) { throw "PrintBridge.App.exe not found at $appExe" }
if (-not (Test-Path $mfDllSrc)) { throw "mfilemon.dll not found at $mfDllSrc (add it to third_party\mfilemon)." }

# --- 1. Trust the test certificate ------------------------------------------
if (Test-Path $certPath) {
    Write-Host "Importing test certificate into trusted stores..."
    Import-Certificate -FilePath $certPath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
    Import-Certificate -FilePath $certPath -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null
} else {
    Write-Warning "Test cert not found at $certPath. If mfilemon.dll is self-signed, run make-test-cert.ps1 first."
}

# --- 2. Warn if test-signing is off (unsigned/self-signed monitor won't load) -
$bcd = bcdedit /enum '{current}' | Out-String
if ($bcd -notmatch "testsigning\s+Yes") {
    Write-Warning "Windows test-signing mode is OFF."
    Write-Warning "To load the self-signed port monitor, run (elevated) then REBOOT:"
    Write-Warning "    bcdedit /set testsigning on"
    Write-Warning "Re-run this script after the reboot."
}

# --- 3. Copy + register the mfilemon port monitor ---------------------------
$monitorsDir = Join-Path $env:SystemRoot "System32\spool\monitors"
Write-Host "Copying mfilemon.dll to $monitorsDir ..."
Copy-Item $mfDllSrc (Join-Path $monitorsDir "mfilemon.dll") -Force

Write-Host "Registering port monitor '$monitorName' via AddMonitor..."
$sig = @'
using System;
using System.Runtime.InteropServices;
public static class PrintMon {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct MONITOR_INFO_2 {
        public string pName;
        public string pEnvironment;
        public string pDLLName;
    }
    [DllImport("winspool.drv", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern bool AddMonitor(string pName, uint Level, ref MONITOR_INFO_2 pMonitors);
}
'@
Add-Type -TypeDefinition $sig -ErrorAction SilentlyContinue
$mi = New-Object PrintMon+MONITOR_INFO_2
$mi.pName = $monitorName
$mi.pEnvironment = if ($is64) { "Windows x64" } else { "Windows NT x86" }
$mi.pDLLName = "mfilemon.dll"
[void][PrintMon]::AddMonitor($null, 2, [ref]$mi)   # ignores "already exists" (1802)

Restart-Service -Name Spooler -Force
Start-Sleep -Seconds 2

# --- 4. Create the mfilemon port that pipes jobs to --capture ---------------
# >>> VERIFY-ON-WINDOWS: mfilemon stores port config under the monitor's Ports key.
# >>> The exact value NAMES below match mfilemon's documented schema; confirm
# >>> against your bundled build and adjust if a value is ignored.
$portsKey = "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\$monitorName\Ports\$portName"
Write-Host "Creating mfilemon port '$portName' -> '$appExe --capture' ..."
New-Item -Path $portsKey -Force | Out-Null
New-ItemProperty -Path $portsKey -Name "OutputPath"      -Value $env:TEMP        -PropertyType String -Force | Out-Null
New-ItemProperty -Path $portsKey -Name "FilePattern"     -Value "pb_%c.ps"       -PropertyType String -Force | Out-Null
New-ItemProperty -Path $portsKey -Name "Overwrite"       -Value 1                -PropertyType DWord  -Force | Out-Null
New-ItemProperty -Path $portsKey -Name "UserCommand"     -Value "`"$appExe`" --capture" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $portsKey -Name "ExecPath"        -Value $InstallRoot     -PropertyType String -Force | Out-Null
New-ItemProperty -Path $portsKey -Name "WaitTermination" -Value 1                -PropertyType DWord  -Force | Out-Null
New-ItemProperty -Path $portsKey -Name "PipeData"        -Value 1                -PropertyType DWord  -Force | Out-Null
Restart-Service -Name Spooler -Force
Start-Sleep -Seconds 2

# --- 5. Add the PostScript virtual printer bound to the port ----------------
$driverName = $null
foreach ($cand in $psDriverCandidates) {
    try {
        Add-PrinterDriver -Name $cand -ErrorAction Stop
        $driverName = $cand
        break
    } catch { }   # try next candidate
}
if (-not $driverName) {
    throw "No suitable in-box PostScript driver found. >>> VERIFY-ON-WINDOWS: list with 'Get-PrinterDriver' and pick a PostScript one."
}
Write-Host "Using driver: $driverName"

if (-not (Get-Printer -Name $printerName -ErrorAction SilentlyContinue)) {
    Add-Printer -Name $printerName -DriverName $driverName -PortName $portName
    Write-Host "Created printer '$printerName'."
} else {
    Write-Host "Printer '$printerName' already exists."
}

Write-Host ""
Write-Host "DONE. Print to '$printerName' from any app to send jobs over the LAN."
