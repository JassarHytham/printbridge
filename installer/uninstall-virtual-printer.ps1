<#
.SYNOPSIS
  Reverses install-virtual-printer.ps1: removes the printer, the mfilemon port,
  the port monitor, and the test certificate. Run elevated (Administrator).
#>

$ErrorActionPreference = "SilentlyContinue"

function Assert-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p  = New-Object Security.Principal.WindowsPrincipal($id)
    if (-not $p.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
        throw "This script must be run as Administrator."
    }
}
Assert-Admin

$monitorName = "PrintBridge File Port Monitor"
$portName    = "PrintBridge:"
$printerName = "Shared Printer (PrintBridge)"

Write-Host "Removing printer '$printerName'..."
Remove-Printer -Name $printerName -ErrorAction SilentlyContinue

Write-Host "Removing port '$portName'..."
# Remove-PrinterPort can be flaky for monitor ports; clear the registry key too.
Remove-PrinterPort -Name $portName -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\$monitorName\Ports\$portName" -ErrorAction SilentlyContinue

Write-Host "Removing port monitor '$monitorName' via DeleteMonitor..."
$sig = @'
using System;
using System.Runtime.InteropServices;
public static class PrintMonDel {
    [DllImport("winspool.drv", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern bool DeleteMonitor(string pName, string pEnvironment, string pMonitorName);
}
'@
Add-Type -TypeDefinition $sig -ErrorAction SilentlyContinue
$env2 = if ([Environment]::Is64BitOperatingSystem) { "Windows x64" } else { "Windows NT x86" }
[void][PrintMonDel]::DeleteMonitor($null, $env2, $monitorName)

Restart-Service -Name Spooler -Force

Write-Host "Removing test certificate from trusted stores..."
Get-ChildItem Cert:\LocalMachine\Root, Cert:\LocalMachine\TrustedPublisher |
    Where-Object { $_.Subject -eq "CN=PrintBridge Test CA" } |
    ForEach-Object { Remove-Item $_.PSPath -Force -ErrorAction SilentlyContinue }

Write-Host "Uninstall complete."
