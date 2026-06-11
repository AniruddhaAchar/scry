<#
.SYNOPSIS
Collect a memory dump from a running .NET process for scry analysis.

.DESCRIPTION
This script collects a memory dump locally so `scry analyze` and `scry stack` can be tested
without a symbol server. Cross-machine dumps require external symbol resolution, which is
not supported in v0.0.1; analyze a dump on the same host that produced it.

Requires the `dotnet-dump` tool globally installed (install with:
    dotnet tool install -g dotnet-dump
).

.PARAMETER ProcessId
The target process ID to capture. Required. (Note: use $ProcessId, not $Pid, which is a
PowerShell built-in variable.)

.PARAMETER OutDir
Output directory for the dump file. Defaults to $env:TEMP.

.EXAMPLE
.\collect-dump.ps1 -ProcessId 1234
# Captures a dump of PID 1234 to %TEMP%\scry-fixture-1234-<timestamp>.dmp

.\collect-dump.ps1 -ProcessId 5678 -OutDir "C:\dumps"
# Captures a dump of PID 5678 to C:\dumps\scry-fixture-5678-<timestamp>.dmp
#>

param(
    [Parameter(Mandatory = $true)]
    [int]$ProcessId,

    [Parameter(Mandatory = $false)]
    [string]$OutDir = $env:TEMP
)

# Validate the process is running
try {
    $proc = Get-Process -Id $ProcessId -ErrorAction Stop
    Write-Host "Found process: $($proc.Name) (PID $ProcessId)"
}
catch {
    Write-Host "Error: Process $ProcessId is not running." -ForegroundColor Red
    exit 1
}

# Build the output path
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$dumpPath = Join-Path $OutDir "scry-fixture-$ProcessId-$timestamp.dmp"

# Run dotnet-dump collect
Write-Host "Collecting dump to: $dumpPath" -ForegroundColor Cyan
try {
    & dotnet-dump collect --process-id $ProcessId --output $dumpPath
}
catch {
    Write-Host "Error: dotnet-dump not found. Install it with:" -ForegroundColor Red
    Write-Host "    dotnet tool install -g dotnet-dump" -ForegroundColor Red
    exit 1
}

if ($LASTEXITCODE -ne 0) {
    Write-Host "dotnet-dump failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

# Output the dump path as the last line (for capture by a caller)
Write-Output $dumpPath
