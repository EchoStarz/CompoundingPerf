<#
.SYNOPSIS
    Build + package CompoundingPerf as a Forge-uploadable .7z archive.

.DESCRIPTION
    Produces `dist/CompoundingPerf-x.y.z.7z` whose internal layout matches what users
    expect when dragging the archive onto `SPT Mods Installer.exe` (or extracting
    manually over the SPT install root):

        BepInEx/plugins/CompoundingPerf.Client/
            CompoundingPerf.Client.dll
        SPT/user/mods/CompoundingPerf/
            CompoundingPerf.dll
            config.json

.PARAMETER RepoRoot
    Defaults to this script's parent directory.

.PARAMETER Version
    Override the version string in the output filename. Defaults to the value parsed
    from CompoundingPerf.csproj `<Version>`.

.PARAMETER SevenZipPath
    Full path to 7z.exe. Defaults to auto-discovery (PATH, then Program Files).

.EXAMPLE
    .\scripts\package-release.ps1
#>
[CmdletBinding()]
param(
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $Version,
    [string] $SevenZipPath
)

$ErrorActionPreference = 'Stop'

function Step([string]$msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function OK  ([string]$msg) { Write-Host "    $msg" -ForegroundColor Green }

# 0. Resolve version from the server csproj unless overridden
if (-not $Version) {
    [xml]$serverCsproj = Get-Content (Join-Path $RepoRoot 'CompoundingPerf.csproj')
    $Version = ("$($serverCsproj.Project.PropertyGroup.Version)").Trim()
    if (-not $Version) { throw "Could not parse <Version> from CompoundingPerf.csproj" }
}
Step "Packaging CompoundingPerf v$Version"

# 1. Clean Release builds (no live deploy — packaging must not depend on a running install)
Step 'Building server (Release)'
& dotnet build (Join-Path $RepoRoot 'CompoundingPerf.csproj') -c Release -p:SkipDeploy=true --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Server build failed' }
OK 'server build ok'

Step 'Building client (Release)'
& dotnet build (Join-Path $RepoRoot 'client\CompoundingPerf.Client.csproj') -c Release -p:SkipDeploy=true --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Client build failed' }
OK 'client build ok'

Step 'Running tests'
& dotnet test (Join-Path $RepoRoot 'tests\CompoundingPerf.Tests.csproj') -c Release -p:SkipDeploy=true --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Tests failed — not packaging a broken build' }
OK 'tests pass'

# 2. Stage the archive layout
$stage = Join-Path $RepoRoot 'dist\stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$serverDir = Join-Path $stage 'SPT\user\mods\CompoundingPerf'
$clientDir = Join-Path $stage 'BepInEx\plugins\CompoundingPerf.Client'
New-Item -ItemType Directory -Force $serverDir | Out-Null
New-Item -ItemType Directory -Force $clientDir | Out-Null

Copy-Item (Join-Path $RepoRoot 'bin\Release\CompoundingPerf\CompoundingPerf.dll') $serverDir
Copy-Item (Join-Path $RepoRoot 'config.json') $serverDir
Copy-Item (Join-Path $RepoRoot 'client\bin\Release\CompoundingPerf.Client.dll') $clientDir
OK 'staged'

# 3. Compress
$distFile = Join-Path $RepoRoot "dist\CompoundingPerf-$Version.7z"
if (Test-Path $distFile) { Remove-Item $distFile -Force }

if (-not $SevenZipPath) {
    $candidates = @(
        (Get-Command 7z.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
        'C:\Program Files\7-Zip\7z.exe',
        'C:\Program Files (x86)\7-Zip\7z.exe'
    ) | Where-Object { $_ -and (Test-Path $_) }
    $SevenZipPath = $candidates | Select-Object -First 1
}

if ($SevenZipPath) {
    Step "Compressing with 7-Zip ($SevenZipPath)"
    Push-Location $stage
    & $SevenZipPath a -t7z -mx9 $distFile '.\*' | Out-Null
    Pop-Location
} else {
    Step '7-Zip not found — falling back to .zip'
    $distFile = Join-Path $RepoRoot "dist\CompoundingPerf-$Version.zip"
    if (Test-Path $distFile) { Remove-Item $distFile -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $distFile
}

Remove-Item $stage -Recurse -Force
OK "wrote $distFile"
Step 'Done. Upload this archive as the GitHub Release asset; point the Forge download link at that asset URL.'
