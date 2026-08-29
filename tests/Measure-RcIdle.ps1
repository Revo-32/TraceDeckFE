param(
    [Parameter(Mandatory = $true)][int]$TargetProcessId,
    [Parameter(Mandatory = $true)][string]$ExpectedExecutablePath,
    [ValidateRange(30, 3600)][int]$DurationSeconds = 600,
    [ValidateRange(1, 60)][int]$SampleIntervalSeconds = 10
)

$ErrorActionPreference = 'Stop'
$expectedPath = (Resolve-Path -LiteralPath $ExpectedExecutablePath).Path
$candidate = Get-Process -Id $TargetProcessId
$startedAt = $candidate.StartTime.ToUniversalTime()
$candidate.Dispose()
$samples = [System.Collections.Generic.List[object]]::new()
$elapsed = [System.Diagnostics.Stopwatch]::StartNew()
while ($true) {
    $candidate = Get-Process -Id $TargetProcessId
    if (-not [string]::Equals($candidate.Path, $expectedPath, [StringComparison]::OrdinalIgnoreCase) -or
        $candidate.StartTime.ToUniversalTime() -ne $startedAt) {
        throw 'Process identity changed; measurement stopped.'
    }
    $sample = [pscustomobject]@{
        Utc = [DateTime]::UtcNow.ToString('o')
        ElapsedSeconds = $elapsed.Elapsed.TotalSeconds
        CpuSeconds = $candidate.TotalProcessorTime.TotalSeconds
        PrivateBytes = $candidate.PrivateMemorySize64
        WorkingSetBytes = $candidate.WorkingSet64
        Handles = $candidate.HandleCount
        Threads = $candidate.Threads.Count
    }
    $samples.Add($sample)
    $candidate.Dispose()
    if ($samples.Count -eq 1 -or $samples.Count % 6 -eq 1) {
        Write-Output ("Sample {0}: {1:N0}s, CPU={2:F3}s, private={3:N0}, handles={4}" -f
            $samples.Count, $sample.ElapsedSeconds, $sample.CpuSeconds, $sample.PrivateBytes, $sample.Handles)
    }
    if ($elapsed.Elapsed.TotalSeconds -ge $DurationSeconds) { break }
    $remaining = $DurationSeconds - $elapsed.Elapsed.TotalSeconds
    # Avoid a zero-millisecond tail spin when the deadline is less than one clock tick away.
    $delayMilliseconds = [int][Math]::Max(15, [Math]::Ceiling(1000 * [Math]::Min($SampleIntervalSeconds, $remaining)))
    Start-Sleep -Milliseconds $delayMilliseconds
}
$elapsed.Stop()
$first = $samples[0]
$last = $samples[$samples.Count - 1]
$seconds = $last.ElapsedSeconds - $first.ElapsedSeconds
[pscustomobject]@{
    Executable = $expectedPath
    ProcessId = $TargetProcessId
    StartedUtc = $first.Utc
    EndedUtc = $last.Utc
    DurationSeconds = $seconds
    SampleCount = $samples.Count
    CpuSeconds = $last.CpuSeconds - $first.CpuSeconds
    OneLogicalCpuPercent = 100 * ($last.CpuSeconds - $first.CpuSeconds) / $seconds
    PrivateBytesStart = $first.PrivateBytes
    PrivateBytesEnd = $last.PrivateBytes
    PrivateBytesMin = ($samples | Measure-Object -Property PrivateBytes -Minimum).Minimum
    PrivateBytesMax = ($samples | Measure-Object -Property PrivateBytes -Maximum).Maximum
    HandleMin = ($samples | Measure-Object -Property Handles -Minimum).Minimum
    HandleMax = ($samples | Measure-Object -Property Handles -Maximum).Maximum
    ThreadMin = ($samples | Measure-Object -Property Threads -Minimum).Minimum
    ThreadMax = ($samples | Measure-Object -Property Threads -Maximum).Maximum
} | ConvertTo-Json
