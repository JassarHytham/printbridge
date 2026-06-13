<#
.SYNOPSIS
  Reverses install-virtual-printer.ps1: removes the virtual printer and its
  Standard TCP/IP RAW port. Run elevated (Administrator).
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

$printerName = "Shared Printer (PrintBridge)"
$portName    = "PrintBridge_RAW"

Write-Host "Removing printer '$printerName'..."
Remove-Printer -Name $printerName -ErrorAction SilentlyContinue

Write-Host "Removing port '$portName'..."
Remove-PrinterPort -Name $portName -ErrorAction SilentlyContinue

Write-Host "Uninstall complete."
