# Builds MicPin.exe (tray app) and MicPinDiag.exe (console diagnostics)
# using the C# compiler that ships with Windows - no SDK, no NuGet, no runtime install.
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src"
$dist = Join-Path $root "dist"
$manifest = Join-Path $root "app.manifest"
$icon = Join-Path $dist "micpin.ico"

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    throw "Cannot find the .NET Framework C# compiler (csc.exe)."
}

if (-not (Test-Path $dist)) {
    New-Item -ItemType Directory -Path $dist | Out-Null
}

$references = @(
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll",
    "/r:System.Runtime.Serialization.dll"
)

$allSources = Get-ChildItem -Path $src -Filter "*.cs" | ForEach-Object { $_.FullName }
$appSources = Get-ChildItem -Path $src -Filter "*.cs" | Where-Object { $_.Name -ne "Diag.cs" } | ForEach-Object { $_.FullName }
$diagSources = @(
    (Join-Path $src "CoreAudio.cs"),
    (Join-Path $src "AudioDevices.cs"),
    (Join-Path $src "Strings.cs"),
    (Join-Path $src "Diag.cs")
)

$exe = Join-Path $dist "MicPin.exe"
$diagExe = Join-Path $dist "MicPinDiag.exe"

Write-Host "Building $exe"
& $csc @("/nologo", "/target:winexe", "/platform:anycpu", "/optimize+", "/out:$exe", "/win32manifest:$manifest") @references @appSources
if ($LASTEXITCODE -ne 0) { throw "Main build failed." }

# First pass has no icon; let the app render one, then rebuild with it embedded.
Write-Host "Generating application icon"
Start-Process -FilePath $exe -ArgumentList "--write-icon", $icon -Wait -WindowStyle Hidden
Write-Host "Rebuilding $exe with icon"
& $csc @("/nologo", "/target:winexe", "/platform:anycpu", "/optimize+", "/out:$exe", "/win32manifest:$manifest", "/win32icon:$icon") @references @appSources
if ($LASTEXITCODE -ne 0) { throw "Icon build failed." }

Write-Host "Building $diagExe"
& $csc @("/nologo", "/target:exe", "/platform:anycpu", "/optimize+", "/out:$diagExe") @references @diagSources
if ($LASTEXITCODE -ne 0) { throw "Diagnostics build failed." }

Write-Host ""
Write-Host "Done:"
Get-ChildItem -Path $dist -Filter "*.exe" | ForEach-Object {
    Write-Host ("  " + $_.FullName + "  (" + [math]::Round($_.Length / 1KB) + " KB)")
}
