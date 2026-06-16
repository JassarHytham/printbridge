<#
.SYNOPSIS
  Installs the "Shared Printer (PrintBridge)" virtual printer.
  Log: C:\ProgramData\PrintBridge\install-printer.log
#>
param([string]$InstallRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = "Stop"

$logDir  = "C:\ProgramData\PrintBridge"
$logFile = "$logDir\install-printer.log"
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Force $logDir | Out-Null }
function Log { param([string]$m)
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $m"
    Write-Host $line; Add-Content $logFile $line -Encoding UTF8 }

Log "=== PrintBridge printer install started ==="
Log "OS: $([Environment]::OSVersion.VersionString)  Arch: $env:PROCESSOR_ARCHITECTURE"

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltinRole]::Administrator)) {
    Log "ERROR: not running as Administrator."; throw "Must run as Administrator." }

$printerName = "Shared Printer (PrintBridge)"
$portName    = "PrintBridge_RAW"
$portAddress = "127.0.0.1"
$portNumber  = 9100

if (Get-Printer -Name $printerName -ErrorAction SilentlyContinue) {
    Log "Removing existing printer..."; Remove-Printer -Name $printerName -ErrorAction SilentlyContinue }
if (Get-PrinterPort -Name $portName -ErrorAction SilentlyContinue) {
    Log "Removing existing port..."; Remove-PrinterPort -Name $portName -ErrorAction SilentlyContinue }

Log "Creating RAW port $portName -> ${portAddress}:$portNumber ..."
try {
    Add-PrinterPort -Name $portName -PrinterHostAddress $portAddress -PortNumber $portNumber
    Log "Port created via Add-PrinterPort."
} catch {
    Log "Add-PrinterPort failed: $($_.Exception.Message) — WMI fallback..."
    $w = ([wmiclass]"Win32_TCPIPPrinterPort").CreateInstance()
    $w.Name=$portName; $w.HostAddress=$portAddress; $w.PortNumber=$portNumber
    $w.Protocol=1; $w.Queue="RAW"; $w.Put() | Out-Null
    Log "Port created via WMI."
}

function TryAddDriver([string]$name) {
    if (Get-PrinterDriver -Name $name -ErrorAction SilentlyContinue) { return $true }
    try { Add-PrinterDriver -Name $name -ErrorAction Stop; return $true } catch { return $false }
}

$driverName = $null
foreach ($cand in @("Microsoft PS Class Driver","MS Publisher Imagesetter",
                    "HP Color LaserJet 2800 Series PS","Generic / Text Only")) {
    Log "Trying driver: $cand"
    if (TryAddDriver $cand) { $driverName = $cand; Log "Driver ready: $driverName"; break }
}

if (-not $driverName) {
    Log "Quick pass failed — staging ntprint.inf via pnputil..."
    $inf = "$env:SystemRoot\inf\ntprint.inf"
    if (Test-Path $inf) {
        $r = & pnputil.exe /add-driver $inf /install 2>&1; Log "pnputil: $r"
        if (TryAddDriver "Microsoft PS Class Driver") { $driverName = "Microsoft PS Class Driver" }
    } else { Log "ntprint.inf not found at $inf" }
}

if (-not $driverName) {
    Log "ERROR: No driver could be installed. Drivers on this machine:"
    Get-PrinterDriver | ForEach-Object { Log "  $($_.Name)" }
    throw "PostScript driver not found. See $logFile"
}

Log "Creating printer '$printerName' with driver '$driverName'..."
Add-Printer -Name $printerName -DriverName $driverName -PortName $portName
Log "SUCCESS. '$printerName' is ready. Run PrintBridge.App.exe before printing."
