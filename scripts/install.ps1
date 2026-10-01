#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs MiniPrinter: the Windows service, the "X5h Thermal Printer" IPP queue and the tray app.

.DESCRIPTION
    1. Publishes (or takes from -SourceDir) the service, tray app and CLI, self-contained win-x64.
    2. Copies them to %ProgramFiles%\MiniPrinter and prepares %ProgramData%\MiniPrinter.
    3. Registers and starts the "MiniPrinter" service (automatic start, restart on failure).
    4. Creates the print queue with the built-in "Microsoft IPP Class Driver" pointing to
       http://127.0.0.1:<IppPort>/ipp/print.
    5. Registers the tray app to start at logon for every user and starts it for the current user.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\install.ps1
#>
[CmdletBinding()]
param(
    [string]$SourceDir,
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'MiniPrinter'),
    [string]$PrinterName = 'X5h Thermal Printer',
    [int]$IppPort = 8631
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dataDir = Join-Path $env:ProgramData 'MiniPrinter'
$serviceName = 'MiniPrinter'
$ippUrl = "http://127.0.0.1:$IppPort/ipp/print"

function Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }

# --- 1. Build -------------------------------------------------------------------------------
if (-not $SourceDir) {
    Step 'Publishing (dotnet publish, self-contained win-x64)'
    $SourceDir = Join-Path $repo 'artifacts\publish'
    if (Test-Path $SourceDir) { Remove-Item $SourceDir -Recurse -Force }
    foreach ($project in 'MiniPrinter.Service', 'MiniPrinter.Tray', 'MiniPrinter.Cli') {
        & dotnet publish (Join-Path $repo "src\$project\$project.csproj") -c Release -r win-x64 --self-contained true `
            -p:PublishReadyToRun=false -o $SourceDir --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish $project failed" }
    }
}
foreach ($exe in 'MiniPrinter.Service.exe', 'MiniPrinter.Tray.exe', 'miniprinter.exe') {
    if (-not (Test-Path (Join-Path $SourceDir $exe))) { throw "$exe not found in $SourceDir" }
}

# --- 2. Stop a previous installation --------------------------------------------------------
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Step 'Stopping existing service'
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}
Get-Process -Name 'MiniPrinter.Tray' -ErrorAction SilentlyContinue | Stop-Process -Force

# --- 3. Copy files and prepare data directory -----------------------------------------------
Step "Copying to $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $SourceDir '*') -Destination $InstallDir -Recurse -Force

Step "Preparing $dataDir"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
# SYSTEM and Administrators: full control. Users: read (the tray app reads the control token).
& icacls $dataDir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' | Out-Null
$settingsPath = Join-Path $dataDir 'settings.json'
if (-not (Test-Path $settingsPath)) {
    @{ ippPort = $IppPort; printerName = $PrinterName } | ConvertTo-Json | Set-Content -Path $settingsPath -Encoding utf8
}

# --- 4. Service -----------------------------------------------------------------------------
$binary = '"' + (Join-Path $InstallDir 'MiniPrinter.Service.exe') + '"'
if (-not $existing) {
    Step 'Registering the MiniPrinter service'
    New-Service -Name $serviceName -BinaryPathName $binary -DisplayName 'MiniPrinter' `
        -Description 'Exposes Bluetooth "cat" thermal printers (X5h-E07A) as a standard IPP printer.' `
        -StartupType Automatic | Out-Null
} else {
    & sc.exe config $serviceName binPath= $binary start= auto | Out-Null
}
& sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
Step 'Starting the service'
Start-Service -Name $serviceName

$deadline = (Get-Date).AddSeconds(30)
do {
    Start-Sleep -Milliseconds 500
    $listening = Test-NetConnection -ComputerName 127.0.0.1 -Port $IppPort -InformationLevel Quiet -WarningAction SilentlyContinue
} until ($listening -or (Get-Date) -gt $deadline)
if (-not $listening) { throw "The service did not open the IPP port $IppPort. See the Application event log (source MiniPrinter)." }

# --- 5. Print queue -------------------------------------------------------------------------
if (Get-Printer -Name $PrinterName -ErrorAction SilentlyContinue) {
    Step "Print queue '$PrinterName' already exists"
} else {
    Step "Creating print queue '$PrinterName' ($ippUrl, Microsoft IPP Class Driver)"
    Add-Printer -Name $PrinterName -IppURL $ippUrl
}

# --- 6. Tray app ----------------------------------------------------------------------------
$tray = Join-Path $InstallDir 'MiniPrinter.Tray.exe'
Step 'Registering the tray app at logon'
New-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name 'MiniPrinter' `
    -Value "`"$tray`"" -PropertyType String -Force | Out-Null
# Start it un-elevated in the interactive session (explorer launches it as the logged-on user).
Start-Process -FilePath explorer.exe -ArgumentList "`"$tray`""

Write-Host ''
Write-Host "MiniPrinter installed." -ForegroundColor Green
Write-Host "  Service : $serviceName (automatic)"
Write-Host "  Printer : $PrinterName -> $ippUrl"
Write-Host "  Tray app: $tray (open it to choose and connect the printer)"
