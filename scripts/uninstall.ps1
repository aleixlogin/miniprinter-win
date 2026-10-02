#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Removes MiniPrinter: print queue, service, firewall rule, tray autostart and program files.

.PARAMETER KeepConfig
    Keep %ProgramData%\MiniPrinter (settings and the selected printer).
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'MiniPrinter'),
    [string]$PrinterName = 'X5h Thermal Printer',
    [switch]$KeepConfig
)

$ErrorActionPreference = 'Continue'
$serviceName = 'MiniPrinter'
function Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }

Step "Removing print queue '$PrinterName'"
$printer = Get-Printer -Name $PrinterName -ErrorAction SilentlyContinue
if ($printer) {
    Get-PrintJob -PrinterName $PrinterName -ErrorAction SilentlyContinue | Remove-PrintJob -ErrorAction SilentlyContinue
    Remove-Printer -Name $PrinterName
    if ($printer.PortName -and (Get-PrinterPort -Name $printer.PortName -ErrorAction SilentlyContinue)) {
        Remove-PrinterPort -Name $printer.PortName -ErrorAction SilentlyContinue
    }
}

Step 'Stopping the tray app'
Get-Process -Name 'MiniPrinter.Tray' -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name 'MiniPrinter' -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('SendTo')) 'MiniPrinter.lnk') -ErrorAction SilentlyContinue

Step 'Removing the service'
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $serviceName | Out-Null
}

Step 'Removing the firewall rule'
& netsh advfirewall firewall delete rule name="MiniPrinter IPP" | Out-Null

Step "Removing $InstallDir"
if (Test-Path $InstallDir) {
    Start-Sleep -Seconds 2   # let the service process exit and release its files
    Remove-Item $InstallDir -Recurse -Force
}

$dataDir = Join-Path $env:ProgramData 'MiniPrinter'
if (-not $KeepConfig -and (Test-Path $dataDir)) {
    Step "Removing $dataDir"
    Remove-Item $dataDir -Recurse -Force
}

Write-Host 'MiniPrinter removed.' -ForegroundColor Green
