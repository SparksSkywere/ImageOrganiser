#Requires -Version 5.1
# Build Image Organiser: portable EXE, Windows Installer, and dist packages in one script.
#
#   .\build.ps1
#   .\build.ps1 -Clean
#   .\build.ps1 -Run
#   .\build.ps1 -Zip
#
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$Clean,
    [switch]$Run,
    [switch]$Zip
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
Set-Location $Root

$AppProj = Join-Path $Root "src\ImageOrganiser\ImageOrganiser.csproj"
$MsiProj = Join-Path $Root "installer\wix\ImageOrganiser.Installer\ImageOrganiser.Installer.wixproj"
$BundleProj = Join-Path $Root "installer\wix\ImageOrganiser.Bundle\ImageOrganiser.Bundle.wixproj"
$PayloadDir = Join-Path $Root "artifacts\payload"
$DistDir = Join-Path $Root "dist"
$InstallerDir = Join-Path $Root "installer"
$IconSrc = Join-Path $Root "assets\installer.ico"
$AppIcon = Join-Path $Root "src\ImageOrganiser\Assets\app.ico"
$LocalBuild = Join-Path $env:LOCALAPPDATA "ImageOrganiser\build"

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host ("==> " + $Message) -ForegroundColor Cyan
}

function Invoke-DotNet([string[]]$DotNetArgs) {
    Write-Host ("    dotnet " + ($DotNetArgs -join " ")) -ForegroundColor DarkGray
    & dotnet @DotNetArgs
    if ($LASTEXITCODE -ne 0) {
        throw ("dotnet " + ($DotNetArgs -join " ") + " failed ($LASTEXITCODE)")
    }
}

function Find-Output([string]$Name, [string[]]$SearchRoots) {
    foreach ($search in $SearchRoots) {
        if (-not (Test-Path $search)) { continue }
        $hit = Get-ChildItem $search -Recurse -Filter $Name -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if ($hit) { return $hit }
    }
    return $null
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK is required. Install from https://dotnet.microsoft.com/download"
}
if (-not (Test-Path $AppProj)) { throw "Project not found: $AppProj" }
if (-not (Test-Path $IconSrc)) { throw "Production icon missing: $IconSrc" }

$csprojText = Get-Content $AppProj -Raw
$AppVersion = "1.0.0"
if ($csprojText -match '<Version>([^<]+)</Version>') {
    $AppVersion = $Matches[1].Trim()
}

Write-Host ""
Write-Host "=== Image Organiser - portable + installer + dist ===" -ForegroundColor Cyan
Write-Host ("Root:          " + $Root)
Write-Host ("Version:       " + $AppVersion)
Write-Host ("Configuration: " + $Configuration)
Write-Host ("Runtime:       " + $Runtime)
Write-Host "Publisher:     Skywere Industries"
Write-Host ""

if ($Clean) {
    Write-Step "Cleaning"
    foreach ($p in @($PayloadDir, $DistDir, $LocalBuild)) {
        if (Test-Path $p) {
            Remove-Item $p -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host ("    removed " + $p) -ForegroundColor DarkGray
        }
    }
}

Write-Step ("Sync icon and installer version " + $AppVersion)
New-Item -ItemType Directory -Path (Split-Path $AppIcon) -Force | Out-Null
Copy-Item $IconSrc $AppIcon -Force
foreach ($wxs in @(
    (Join-Path $Root "installer\wix\ImageOrganiser.Installer\Package.wxs"),
    (Join-Path $Root "installer\wix\ImageOrganiser.Bundle\Bundle.wxs")
)) {
    if (-not (Test-Path $wxs)) { continue }
    $text = Get-Content $wxs -Raw
    $updated = [regex]::Replace($text, 'Version="\d+\.\d+\.\d+"', ('Version="{0}"' -f $AppVersion), 1)
    if ($updated -ne $text) {
        Set-Content -Path $wxs -Value $updated -Encoding UTF8 -NoNewline
        Write-Host ("    synced " + (Split-Path $wxs -Leaf)) -ForegroundColor DarkGray
    }
}

$objDir = Join-Path $LocalBuild "obj\ImageOrganiser"
if (Test-Path $objDir) {
    Get-ChildItem $objDir -Recurse -Filter "apphost.exe" -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

if (Test-Path $PayloadDir) { Remove-Item $PayloadDir -Recurse -Force }
New-Item -ItemType Directory -Path $PayloadDir -Force | Out-Null
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null
New-Item -ItemType Directory -Path $InstallerDir -Force | Out-Null

Write-Step ("Publish self-contained single-file (" + $Runtime + ")")
Invoke-DotNet @(
    "publish", $AppProj,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=none",
    "-p:DebugSymbols=false",
    "-o", $PayloadDir
)

$publishedExe = Join-Path $PayloadDir "ImageOrganiser.exe"
if (-not (Test-Path $publishedExe)) { throw "Missing ImageOrganiser.exe after publish." }

Get-ChildItem $PayloadDir -Filter "*.pdb" -Recurse -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

$iconDstDir = Join-Path $PayloadDir "Assets"
New-Item -ItemType Directory -Path $iconDstDir -Force | Out-Null
Copy-Item $IconSrc (Join-Path $iconDstDir "app.ico") -Force
Copy-Item $IconSrc (Join-Path $PayloadDir "app.ico") -Force
$payloadMb = [math]::Round(((Get-ChildItem $PayloadDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host ("    payload " + $payloadMb + " MB") -ForegroundColor DarkGray

Write-Step "Portable distribution"
$staleDir = Join-Path $DistDir "ImageOrganiser"
if ((Test-Path $staleDir) -and (Get-Item $staleDir).PSIsContainer) {
    Remove-Item $staleDir -Recurse -Force
}
$portableDir = Join-Path $DistDir "portable"
if (Test-Path $portableDir) { Remove-Item $portableDir -Recurse -Force }
New-Item -ItemType Directory -Path $portableDir -Force | Out-Null
Copy-Item $publishedExe (Join-Path $portableDir "ImageOrganiser.exe") -Force
Copy-Item (Join-Path $PayloadDir "app.ico") (Join-Path $portableDir "app.ico") -Force
Copy-Item $publishedExe (Join-Path $DistDir "ImageOrganiser.exe") -Force
Write-Host "    dist\ImageOrganiser.exe" -ForegroundColor DarkGray
Write-Host "    dist\portable\ImageOrganiser.exe" -ForegroundColor DarkGray

Write-Step "MSI (Windows Installer)"
Get-ChildItem (Join-Path $Root "installer\wix\ImageOrganiser.Installer\bin") -Recurse -Filter "*.msi" -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue
Invoke-DotNet @("build", $MsiProj, "-c", $Configuration, "-p:PayloadDir=$PayloadDir", "--no-incremental")

$msi = Find-Output "ImageOrganiser.msi" @(
    (Join-Path $Root "installer\wix\ImageOrganiser.Installer"),
    $LocalBuild
)
if (-not $msi) { throw "ImageOrganiser.msi was not produced." }
Copy-Item $msi.FullName (Join-Path $DistDir "ImageOrganiser.msi") -Force
Copy-Item $msi.FullName (Join-Path $InstallerDir "ImageOrganiser.msi") -Force
$msiMb = [math]::Round(($msi.Length / 1MB), 1)
Write-Host ("    dist\ImageOrganiser.msi  (" + $msiMb + " MB)") -ForegroundColor DarkGray

Write-Step "Setup.exe (WiX Burn)"
Get-ChildItem (Join-Path $Root "installer\wix\ImageOrganiser.Bundle\bin") -Recurse -Filter "*.exe" -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue
Invoke-DotNet @("build", $BundleProj, "-c", $Configuration, "-p:PayloadDir=$PayloadDir", "--no-incremental")

$setup = Find-Output "ImageOrganiser-Setup.exe" @(
    (Join-Path $Root "installer\wix\ImageOrganiser.Bundle"),
    $LocalBuild
)
if (-not $setup) { throw "ImageOrganiser-Setup.exe was not produced." }
Copy-Item $setup.FullName (Join-Path $DistDir "ImageOrganiser-Setup.exe") -Force
Copy-Item $setup.FullName (Join-Path $InstallerDir "ImageOrganiser-Setup.exe") -Force
$setupMb = [math]::Round(($setup.Length / 1MB), 1)
Write-Host ("    dist\ImageOrganiser-Setup.exe  (" + $setupMb + " MB)") -ForegroundColor DarkGray

$readme = @(
    ("Image Organiser " + $AppVersion),
    "Skywere Industries",
    "",
    "This folder is the full distribution.",
    "",
    "Portable",
    "--------",
    "ImageOrganiser.exe          self-contained, no install, no .NET runtime required",
    "portable\ImageOrganiser.exe same copy, in a folder you can zip and move",
    "",
    "Installer (Start Menu + optional Desktop shortcut)",
    "--------------------------------------------------",
    "ImageOrganiser-Setup.exe    recommended; launches the MSI wizard",
    "ImageOrganiser.msi          Windows Installer (folder + Start Menu + Desktop feature)",
    "",
    "Default install folder:  Program Files\Skywere Industries\ImageOrganiser",
    "Start Menu:              Skywere Industries \ Image Organiser",
    "",
    "  ImageOrganiser-Setup.exe",
    "  msiexec /i ImageOrganiser.msi",
    "",
    "Build",
    "-----",
    "From the repo:  .\build.ps1",
    "Intermediates:  %LOCALAPPDATA%\ImageOrganiser\build\"
) -join "`r`n"
Set-Content -Path (Join-Path $DistDir "README.txt") -Value $readme -Encoding UTF8

if ($Zip) {
    Write-Step "Zip portable package"
    $zipPath = Join-Path $DistDir ("ImageOrganiser-" + $AppVersion + "-" + $Runtime + ".zip")
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $DistDir "ImageOrganiser.exe"), (Join-Path $DistDir "README.txt") `
        -DestinationPath $zipPath -Force
    Write-Host ("    " + $zipPath) -ForegroundColor DarkGray
}

$exeMb = [math]::Round(((Get-Item (Join-Path $DistDir "ImageOrganiser.exe")).Length / 1MB), 1)
Write-Host ""
Write-Host "=== Build succeeded ===" -ForegroundColor Green
Write-Host ("  dist\ImageOrganiser.exe          portable  (" + $exeMb + " MB)")
Write-Host "  dist\portable\                   portable folder"
Write-Host "  dist\ImageOrganiser.msi          Windows Installer"
Write-Host "  dist\ImageOrganiser-Setup.exe    Setup (Start Menu)"
Write-Host "  dist\README.txt"
Write-Host ""

if ($Run) {
    Write-Step "Launch portable EXE"
    Start-Process -FilePath (Join-Path $DistDir "ImageOrganiser.exe")
}
