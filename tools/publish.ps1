<#
    publish.ps1 -- builds the single-file Big2.exe into dist/.

    Repo-relative throughout, so it survives being cloned somewhere else.

    It prints the built timestamp against the newest source file and FAILS when
    the output is behind. That check exists because the most expensive failure
    mode on a project like this is the owner playing a stale published binary
    while being told a fix has landed: `dotnet build` updates bin/ and never
    touches dist/, so the two drift apart silently and both parties are right
    about different executables.
#>

[CmdletBinding()]
param(
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root    = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'Big2.App\Big2.App.csproj'
$dist    = Join-Path $root 'dist'

Write-Host "Publishing $project"

dotnet publish $project -c $Configuration -o $dist --nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed with exit code $LASTEXITCODE" }

$exe = Join-Path $dist 'Big2.exe'
if (-not (Test-Path $exe)) { throw "expected $exe to exist after publishing" }

$sourceDirs = @((Join-Path $root 'Big2.App'), (Join-Path $root 'Big2.Core'))
$newest = Get-ChildItem $sourceDirs -Recurse -Include *.cs, *.xaml, *.csproj |
          Where-Object { $_.FullName -split '\\' -notcontains 'obj' -and
                         $_.FullName -split '\\' -notcontains 'bin' } |
          Sort-Object LastWriteTime -Descending | Select-Object -First 1

$built = (Get-Item $exe).LastWriteTime

Write-Host ""
Write-Host ("output        {0}" -f $exe)
Write-Host ("size          {0:N1} MB" -f ((Get-Item $exe).Length / 1MB))
Write-Host ("built         {0}" -f $built)
Write-Host ("newest source {0}  ({1})" -f $newest.LastWriteTime, $newest.Name)

if ($newest.LastWriteTime -gt $built) {
    Write-Warning "The published binary is OLDER than the newest source file. Do not test this build."
    exit 1
}

# A genuine single-file publish leaves the executable alone in dist/. Anything
# else beside it means a property is missing -- most likely
# IncludeNativeLibrariesForSelfExtract (WPF ships native interop DLLs that plain
# single-file bundling cannot fold in on its own, and a plain publish leaves five
# loose files) or DebugType=embedded, which otherwise leaves a stray .pdb.
#
# big2.ini is excluded because the game writes it beside itself on exit, so it
# turns up in dist/ as soon as the published build has been run once. That is
# runtime state, not a build output, and flagging it would train the reader to
# ignore this warning.
$ignore = @('Big2.exe', 'big2.ini')
$strays = Get-ChildItem $dist -File | Where-Object { $_.Name -notin $ignore }
if ($strays) {
    Write-Host ""
    Write-Warning "dist/ contains files besides the executable:"
    $strays | ForEach-Object { Write-Host "  $($_.Name)" }
    exit 1
}

Write-Host ""
Write-Host "dist/ contains the executable and nothing else."
