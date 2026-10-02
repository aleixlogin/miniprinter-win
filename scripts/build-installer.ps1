<#
.SYNOPSIS
    Builds MiniPrinter-Setup-<version>.exe: tests, framework-dependent publish and Inno Setup.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1 -Version 0.4.0
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
function Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }

if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $repo 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must be X.Y.Z (got '$Version')." }

# Locate the Inno Setup compiler.
$iscc = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) not found. Install it with: winget install JRSoftware.InnoSetup' }

if (-not $SkipTests) {
    Step 'Running tests'
    & dotnet test (Join-Path $repo 'MiniPrinter.slnx') -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

Step "Publishing $Version (framework-dependent, win-x64)"
$publish = Join-Path $repo 'artifacts\publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
foreach ($project in 'MiniPrinter.Service', 'MiniPrinter.Tray', 'MiniPrinter.Cli') {
    & dotnet publish (Join-Path $repo "src\$project\$project.csproj") -c Release -r win-x64 --self-contained false `
        -p:Version=$Version -o $publish --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish $project failed." }
}

Step 'Compiling installer'
& $iscc "/DAppVersion=$Version" /Q (Join-Path $repo 'installer\MiniPrinter.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC failed.' }

$setup = Join-Path $repo "artifacts\installer\MiniPrinter-Setup-$Version.exe"
Write-Host ''
Write-Host "Installer: $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB)" -ForegroundColor Green
