#requires -Version 7
<#
.SYNOPSIS
  Manual end-to-end smoke test for the single `scry` binary.

.DESCRIPTION
  Collects a local dump of a throwaway process, then exercises every command against the
  built `scry` and asserts each one behaves. This is the manual counterpart to the fast
  unit tests (`dotnet test scry.slnx --filter Category=Unit`); it validates the things unit
  tests can't — real ClrMD analysis and the self-spawned host process (ADR 0007).

  Steps: analyze -> ps -> health -> stack -> clrthreads -> dumpheap (stat) ->
  dumpheap --type (paged) -> dumpexceptions -> printexception (found + not-found) ->
  dumpobject -> dumparray -> gcroot -> syncblk -> dumpasync -> pipe-hang check -> stop.
  Exits non-zero if any step fails. Always stops the session and deletes the dump on exit.

.PARAMETER Scry
  Path to the built scry executable. Defaults to the Debug build at the repo root.

.PARAMETER OutDir
  Directory for the collected fixture dump. Defaults to $env:TEMP.

.EXAMPLE
  pwsh ./eng/scripts/smoke.ps1
  pwsh ./eng/scripts/smoke.ps1 -Scry ./artifacts/win-x64/scry.exe
#>
param(
    [string]$Scry = (Join-Path $PSScriptRoot '..\..\src\Scry.Client\bin\Debug\net10.0\scry.exe'),
    [string]$OutDir = $env:TEMP
)

$ErrorActionPreference = 'Stop'
$script:failed = 0
$script:strAddr = $null
$script:exAddr = $null

function Step([string]$name, [scriptblock]$body) {
    Write-Host "`n=== $name ===" -ForegroundColor Cyan
    try {
        & $body
        Write-Host "PASS: $name" -ForegroundColor Green
    }
    catch {
        Write-Host "FAIL: $name -> $_" -ForegroundColor Red
        $script:failed++
    }
}

# Run a scry command, fail if it exits non-zero, and return its stdout (JSON).
function Run([string[]]$ScryArgs) {
    $out = & $Scry @ScryArgs
    if ($LASTEXITCODE -ne 0) { throw "scry $($ScryArgs -join ' ') exited $LASTEXITCODE`n$out" }
    return $out
}

if (-not (Test-Path $Scry)) {
    throw "scry not found at '$Scry' — build first: dotnet build scry.slnx"
}
$Scry = (Resolve-Path $Scry).Path
Write-Host "Using scry: $Scry"

# --- Arrange: spawn a throwaway .NET process and dump it ---
$collect = Join-Path $PSScriptRoot '..\..\scripts\collect-dump.ps1'
$victim = Start-Process pwsh -ArgumentList '-NoProfile', '-Command', 'Start-Sleep -Seconds 300' -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 2
$dump = & $collect -ProcessId $victim.Id -OutDir $OutDir | Select-Object -Last 1
$victim | Stop-Process -Force
if (-not (Test-Path $dump)) { throw "dump was not collected" }
Write-Host "Dump: $dump"

try {
    Step 'analyze (self-spawns host)' {
        $r = Run @('analyze', $dump) | ConvertFrom-Json
        if ($r.state -ne 'READY') { throw "state=$($r.state)" }
        if (-not $r.runtimeVersion) { throw 'no runtimeVersion' }
        Write-Host "  handle=$($r.handle) pid=$($r.pid) rt=$($r.runtimeVersion)"
    }

    Step 'ps (host process is `scry` in __host mode)' {
        $r = Run @('ps') | ConvertFrom-Json
        if ($r.sessions.Count -ne 1) { throw "expected 1 session, got $($r.sessions.Count)" }
        $pn = (Get-Process -Id $r.sessions[0].pid).ProcessName
        if ($pn -ne 'scry') { throw "host process is '$pn', expected 'scry'" }
        Write-Host "  pid=$($r.sessions[0].pid) procName=$pn"
    }

    Step 'health' {
        $r = Run @('health') | ConvertFrom-Json
        if ($r.state -ne 'READY') { throw "state=$($r.state)" }
    }

    Step 'stack (all threads)' {
        $r = Run @('stack') | ConvertFrom-Json
        if ($r.threads.Count -lt 1) { throw 'no threads' }
        Write-Host "  threads=$($r.threads.Count)"
    }

    Step 'clrthreads (managed thread list)' {
        $r = Run @('clrthreads') | ConvertFrom-Json
        if ($r.threads.Count -lt 1) { throw 'no threads' }
        $t = $r.threads[0]
        if (-not $t.gcMode) { throw 'no gcMode' }
        Write-Host "  threads=$($r.threads.Count) first: osId=$($t.osThreadId) gcMode=$($t.gcMode) state=$($t.state -join '|')"
    }

    Step 'dumpheap (stat)' {
        $r = Run @('dumpheap') | ConvertFrom-Json
        if ($r.stats.Count -lt 1) { throw 'no stats' }
        Write-Host "  types=$($r.stats.Count) top=$($r.stats[0].type)"
    }

    Step 'dumpheap --type (paged listing)' {
        $r = Run @('dumpheap', '--type', 'System.String', '--limit', '5') | ConvertFrom-Json
        if ($r.objects.Count -lt 1) { throw 'no objects' }
        Write-Host "  totalMatches=$($r.totalMatches) truncated=$($r.truncated) returned=$($r.objects.Count)"
        $script:strAddr = $r.objects[0].address
    }

    Step 'dumpexceptions' {
        $r = Run @('dumpexceptions') | ConvertFrom-Json
        Write-Host "  totalMatches=$($r.totalMatches)"
        if ($r.exceptions.Count -gt 0) { $script:exAddr = $r.exceptions[0].address }
    }

    Step 'printexception (real exception => found:true)' {
        if (-not $script:exAddr) { Write-Host '  (no exceptions in this dump; skipping)'; return }
        $r = Run @('printexception', '--address', $script:exAddr) | ConvertFrom-Json
        if (-not $r.found) { throw "expected found:true for $($script:exAddr)" }
        Write-Host "  type=$($r.type) hResult=$($r.hResult)"
    }

    Step 'printexception (non-exception => found:false)' {
        $r = Run @('printexception', '--address', $script:strAddr) | ConvertFrom-Json
        if ($r.found) { throw 'expected found:false for a String address' }
    }

    Step 'dumpobject (object fields)' {
        $r = Run @('dumpobject', '--address', $script:strAddr) | ConvertFrom-Json
        if (-not $r.found) { throw "expected found:true for $($script:strAddr)" }
        if ($r.fields.Count -lt 1) { throw 'no fields' }
        Write-Host "  type=$($r.type) fields=$($r.fields.Count)"
    }

    Step 'dumparray (array elements)' {
        $arr = (Run @('dumpheap', '--type', 'System.Object[]', '--limit', '1') | ConvertFrom-Json).objects
        if (-not $arr -or $arr.Count -lt 1) { Write-Host '  (no Object[] in this dump; skipping)'; return }
        $r = Run @('dumparray', '--address', $arr[0].address, '--limit', '5') | ConvertFrom-Json
        if (-not $r.found) { throw "expected found:true for an array address" }
        Write-Host "  type=$($r.type) elementType=$($r.elementType) length=$($r.length)"
    }

    Step 'gcroot (root paths for a live object)' {
        $r = Run @('gcroot', '--address', $script:strAddr) | ConvertFrom-Json
        if (-not $r.found) { throw "expected found:true for $($script:strAddr)" }
        Write-Host "  rooted=$($r.rooted) truncated=$($r.truncated) paths=$($r.roots.Count)"
        if ($r.rooted -and $r.roots[0].chain.Count -lt 1) { throw 'rooted path has empty chain' }
    }

    Step 'gcroot (invalid address => found:false)' {
        $r = Run @('gcroot', '--address', '0xdeadbeef') | ConvertFrom-Json
        if ($r.found) { throw 'expected found:false for a bogus address' }
    }

    Step 'syncblk (sync block listing)' {
        # The generic victim may hold no contended monitors; assert shape, not contents.
        $r = Run @('syncblk') | ConvertFrom-Json
        if ($null -eq $r.blocks) { throw 'no blocks array' }
        Write-Host "  monitorBlocks=$($r.blocks.Count)"
    }

    Step 'dumpasync (async state machines)' {
        $r = Run @('dumpasync') | ConvertFrom-Json
        if ($null -eq $r.machines) { throw 'no machines array' }
        Write-Host "  totalMatches=$($r.totalMatches) returned=$($r.machines.Count)"
    }

    Step 'no pipe-hang (dumpheap | Out-String returns promptly)' {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        & $Scry dumpheap | Out-String | Out-Null
        $code = $LASTEXITCODE
        $sw.Stop()
        if ($code -ne 0) { throw "exit $code" }
        if ($sw.ElapsedMilliseconds -gt 10000) { throw "took $($sw.ElapsedMilliseconds) ms (possible hang)" }
        Write-Host "  $($sw.ElapsedMilliseconds) ms"
    }

    Step 'stop (graceful)' {
        $r = Run @('stop') | ConvertFrom-Json
        if ($r.stopped -ne 'graceful') { throw "stopped=$($r.stopped)" }
    }
}
finally {
    # Best-effort cleanup: stop any lingering session, delete the dump.
    & $Scry stop *> $null
    Remove-Item -LiteralPath $dump -ErrorAction SilentlyContinue
}

Write-Host ''
if ($script:failed -gt 0) {
    Write-Host "SMOKE FAILED: $($script:failed) step(s) failed" -ForegroundColor Red
    exit 1
}
Write-Host 'SMOKE PASSED' -ForegroundColor Green
exit 0
