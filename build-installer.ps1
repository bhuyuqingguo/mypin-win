# Builds dist\MicPinSetup.exe: a per-user installer with MicPin.exe embedded as a resource.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root "dist"
$app = Join-Path $dist "MicPin.exe"
$icon = Join-Path $dist "micpin.ico"
$manifest = Join-Path $root "app.manifest"
$installerSource = Join-Path $root "installer\Installer.cs"
$setup = Join-Path $dist "MicPinSetup.exe"

function Find-Csc {
    $candidates = @(
        (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
        (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }
    throw "Cannot find the .NET Framework C# compiler (csc.exe)."
}

if (-not (Test-Path $app)) {
    Write-Host "MicPin.exe not found, building it first"
    & (Join-Path $root "build.ps1")
}

$csc = Find-Csc
$manifestArg = "/win32manifest:" + $manifest
$iconArg = "/win32icon:" + $icon
$resourceArg = "/resource:" + $app + ",MicPinPayload.exe"

$compilerArgs = @(
    "/nologo", "/target:winexe", "/platform:anycpu", "/optimize+", "/out:$setup",
    $manifestArg, $iconArg, $resourceArg,
    "/r:System.dll", "/r:System.Core.dll", "/r:System.Drawing.dll", "/r:System.Windows.Forms.dll",
    $installerSource
)

Write-Host "Building $setup"
& $csc @compilerArgs
if ($LASTEXITCODE -ne 0) { throw "Installer build failed." }

$size = [math]::Round((Get-Item $setup).Length / 1KB)
Write-Host ""
Write-Host "Done:"
Write-Host "  $setup  ($size KB)"
