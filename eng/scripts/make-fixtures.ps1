#requires -Version 7
<#
.SYNOPSIS
  Build the fixture-victim and snapshot one dump per failure mode (hang / idle / leak).

.DESCRIPTION
  Produces the fixture dumps the scry skill evals reference (skills/scry/evals/evals.json).
  Dumps are git-ignored (*.dmp) — regenerate them locally with this script. Each mode:
    hang -> contended monitor + a parked async await  (syncblk + dumpasync)
    idle -> a healthy, idle process                   (the "not enough information" fixture)
    leak -> an unbounded static List<byte[]>          (dumpheap --stat + gcroot)

.PARAMETER OutDir
  Where to write <mode>.dmp. Defaults to the skill eval workspace fixtures dir.

.EXAMPLE
  pwsh ./eng/scripts/make-fixtures.ps1
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\..\skills\scry-workspace\fixtures')
)

$ErrorActionPreference = 'Stop'
$victimDir = Join-Path $PSScriptRoot '..\..\samples\fixture-victim'
$collect   = Join-Path $PSScriptRoot '..\..\scripts\collect-dump.ps1'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Write-Host "Building fixture-victim..." -ForegroundColor Cyan
dotnet build $victimDir -c Release | Out-Null
$exe = Join-Path $victimDir 'bin\Release\net10.0\victim.exe'
if (-not (Test-Path $exe)) { throw "victim build output not found at $exe" }

function Make-Dump([string]$mode, [int]$warmupSeconds) {
    $p = Start-Process $exe -ArgumentList $mode -PassThru -WindowStyle Hidden
    try {
        Start-Sleep -Seconds $warmupSeconds
        $raw = & $collect -ProcessId $p.Id -OutDir $OutDir | Select-Object -Last 1
    }
    finally {
        $p | Stop-Process -Force -ErrorAction SilentlyContinue
    }
    $dest = Join-Path $OutDir "$mode.dmp"
    Move-Item -LiteralPath $raw -Destination $dest -Force
    $mb = [math]::Round((Get-Item $dest).Length / 1MB, 1)
    Write-Host ("  {0,-5} -> {1} ({2} MB)" -f $mode, $dest, $mb) -ForegroundColor Green
}

Make-Dump 'hang' 2
Make-Dump 'idle' 2
Make-Dump 'leak' 5   # let the leak grow before snapshotting

Write-Host "`nFixtures written to $OutDir" -ForegroundColor Cyan
