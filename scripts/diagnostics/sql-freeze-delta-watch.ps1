#Requires -Version 5.1
<#
.SYNOPSIS
  SQL Server freeze / stall watcher - delta waits, active requests, scheduler pressure, IO stall deltas.

.DESCRIPTION
  Samples DMVs on a fixed interval and logs DELTA wait/IO stats (not cumulative totals).
  Also records actual loop elapsed time vs target interval (detects script/CPU/SQL blocking the loop).

  Log: C:\temp\sql-freeze-delta-watch.log

  No application code changes; run manually during a slow UI burst.

.EXAMPLE
  .\sql-freeze-delta-watch.ps1 -ServerInstance "localhost" -Database "VeterinerDb"

.EXAMPLE
  .\sql-freeze-delta-watch.ps1 -ConnectionString "Server=.\SQLEXPRESS;Database=Veteriner;Trusted_Connection=True;TrustServerCertificate=True"
#>
[CmdletBinding()]
param(
    [string] $ServerInstance = "localhost",
    [string] $Database = "",
    [string] $ConnectionString = "",
    [int] $IntervalSeconds = 1,
    [string] $LogPath = "C:\temp\sql-freeze-delta-watch.log",
    [int] $TopWaits = 15,
    [int] $TopRequests = 20,
    [switch] $IncludeSystemSessions
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# --- Benign / background waits (excluded from TOP delta wait list) ---
$BenignWaitPatterns = @(
    '^XE_TIMER_EVENT$',
    '^XE_DISPATCHER_WAIT$',
    '^XE_LIVE_TARGET_TVF$',
    '^SQLTRACE_INCREMENTAL_FLUSH_SLEEP$',
    '^SQLTRACE_WAIT_ENTRIES$',
    '^REQUEST_FOR_DEADLOCK_SEARCH$',
    '^SP_SERVER_DIAGNOSTICS_SLEEP$',
    '^BROKER_.*',
    '^QDS_.*',
    '^HADR_FILESTREAM_IOMGR_IOCOMPLETION$',
    '^HADR_WORK_QUEUE$',
    '^HADR_NOTIFICATION_DEQUEUE$',
    '^CLR_AUTO_EVENT$',
    '^CLR_MANUAL_EVENT$',
    '^DIRTY_PAGE_POLL$',
    '^SLEEP_.*',
    '^ONDEMAND_TASK_QUEUE$',
    '^LAZYWRITER_SLEEP$',
    '^LOGMGR_QUEUE$',
    '^CHECKPOINT_QUEUE$',
    '^RESOURCE_QUEUE$',
    '^WAITFOR$',
    '^SOS_SCHEDULER_YIELD$'
)

function Test-BenignWait {
    param([string] $WaitType)
    foreach ($pat in $BenignWaitPatterns) {
        if ($WaitType -match $pat) { return $true }
    }
    return $false
}

function Get-SqlConnectionString {
    if ($ConnectionString) { return $ConnectionString }
    $builder = "Server=$ServerInstance;"
    if ($Database) { $builder += "Database=$Database;" }
    $builder += "TrustServerCertificate=True;Encrypt=False;Connection Timeout=5;"
    $builder += "Application Name=sql-freeze-delta-watch;"
    # Windows auth default
    if ($env:SQLFREEZE_SQL_USER) {
        $builder += "User ID=$($env:SQLFREEZE_SQL_USER);Password=$($env:SQLFREEZE_SQL_PASSWORD);"
    } else {
        $builder += "Integrated Security=True;"
    }
    return $builder
}

function Invoke-Sql {
    param([string] $Query)
    $connStr = Get-SqlConnectionString
    $conn = New-Object System.Data.SqlClient.SqlConnection $connStr
    try {
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $Query
        $cmd.CommandTimeout = 30
        $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
        $table = New-Object System.Data.DataTable
        [void]$adapter.Fill($table)
        # DataTable implements IEnumerable (rows). Plain "return $table" emits DataRow(s)
        # and a single-row result binds as DataRow to callers — breaking Format-TableForLog.
        return ,$table
    } finally {
        if ($conn.State -eq 'Open') { $conn.Close() }
        $conn.Dispose()
    }
}

function Format-TableForLog {
    param([System.Data.DataTable] $Table, [int] $MaxRows = 50)
    # Defensive: callers must pass DataTable; a lone DataRow is coerced via its parent table.
    if ($Table -is [System.Data.DataRow]) {
        $Table = $Table.Table
    }
    if ($null -eq $Table -or $Table.Rows.Count -eq 0) { return "(none)" }
    $lines = New-Object System.Collections.Generic.List[string]
    $cols = $Table.Columns | ForEach-Object { $_.ColumnName }
    $lines.Add(($cols -join " | "))
    $lines.Add(("-".PadRight(($cols -join " | ").Length, '-')))
    $take = [Math]::Min($MaxRows, $Table.Rows.Count)
    for ($i = 0; $i -lt $take; $i++) {
        $row = $Table.Rows[$i]
        $vals = foreach ($c in $cols) {
            $v = $row[$c]
            if ($null -eq $v -or $v -is [DBNull]) { "" }
            elseif ($v -is [DateTime]) { $v.ToString("o") }
            else { "$v" }
        }
        $lines.Add(($vals -join " | "))
    }
    if ($Table.Rows.Count -gt $take) {
        $lines.Add("... ($($Table.Rows.Count - $take) more rows)")
    }
    return ($lines -join [Environment]::NewLine)
}

function Write-LogBlock {
    param([string] $Text)
    Add-Content -Path $LogPath -Value $Text -Encoding UTF8
}

$includeSystemSessionsInt = if ($IncludeSystemSessions) { 1 } else { 0 }

# --- DMV queries ---
$QueryRequests = @"
SELECT
    r.session_id,
    r.status,
    r.command,
    r.wait_type,
    r.wait_time AS wait_time_ms,
    r.blocking_session_id,
    r.cpu_time AS cpu_time_ms,
    r.logical_reads,
    r.reads,
    r.writes,
    r.total_elapsed_time AS elapsed_ms,
    DB_NAME(r.database_id) AS database_name,
    s.login_name,
    s.host_name,
    s.program_name,
    SUBSTRING(
        t.text,
        (r.statement_start_offset / 2) + 1,
        CASE r.statement_end_offset
            WHEN -1 THEN DATALENGTH(t.text)
            ELSE (r.statement_end_offset - r.statement_start_offset) / 2 + 1
        END
    ) AS statement_text
FROM sys.dm_exec_requests r
INNER JOIN sys.dm_exec_sessions s ON r.session_id = s.session_id
OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) t
WHERE r.session_id <> @@SPID
  AND r.session_id > 50
  AND (
        ($includeSystemSessionsInt) = 1
        OR (
            s.is_user_process = 1
            AND s.program_name NOT LIKE 'sql-freeze-delta-watch%'
        )
    )
ORDER BY r.total_elapsed_time DESC;
"@

$QuerySchedulers = @"
SELECT
    SUM(CAST(runnable_tasks_count AS bigint)) AS runnable_tasks_sum,
    SUM(CAST(current_tasks_count AS bigint)) AS current_tasks_sum,
    SUM(CAST(active_workers_count AS bigint)) AS active_workers_sum,
    SUM(CAST(work_queue_count AS bigint)) AS work_queue_sum,
    COUNT(*) AS scheduler_count
FROM sys.dm_os_schedulers
WHERE scheduler_id < 255;
"@

$QueryWaitStats = @"
SELECT
    wait_type,
    waiting_tasks_count,
    wait_time_ms,
    signal_wait_time_ms
FROM sys.dm_os_wait_stats;
"@

$QueryIoStats = @"
SELECT
    database_id,
    file_id,
    num_of_reads,
    num_of_writes,
    io_stall_read_ms,
    io_stall_write_ms,
    io_stall AS io_stall_total_ms
FROM sys.dm_io_virtual_file_stats(NULL, NULL);
"@

# --- Init log ---
$logDir = Split-Path -Parent $LogPath
if ($logDir -and -not (Test-Path $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}

$header = @"
================================================================================
sql-freeze-delta-watch started
  Time (local): $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')
  Server: $(if ($ConnectionString) { '(connection string)' } else { $ServerInstance })
  Database: $(if ($Database) { $Database } else { '(default)' })
  IntervalSeconds (target): $IntervalSeconds
  LogPath: $LogPath
  IncludeSystemSessions: $IncludeSystemSessions
================================================================================
"@
Set-Content -Path $LogPath -Value $header -Encoding UTF8

# Baseline snapshots (cumulative DMVs)
$prevWaits = $null
$prevIo = $null
$prevTick = [Diagnostics.Stopwatch]::StartNew()
$loopIndex = 0

Write-Host "Logging to $LogPath - Ctrl+C to stop." -ForegroundColor Cyan

while ($true) {
    $loopIndex++
    $tickStart = Get-Date
    $swLoop = [Diagnostics.Stopwatch]::StartNew()

    $loopDelayMs = if ($loopIndex -eq 1) { 0 } else { $prevTick.ElapsedMilliseconds }
    $prevTick.Restart()

    $delayNote = ""
    if ($loopIndex -gt 1 -and $IntervalSeconds -gt 0) {
        $expectedMs = $IntervalSeconds * 1000
        $skewMs = $loopDelayMs - $expectedMs
        if ($skewMs -gt 500) {
            $delayNote = " *** LOOP SKEW: actual interval $($loopDelayMs)ms vs target $($expectedMs)ms (skew +$($skewMs)ms) - collector blocked or machine under load ***"
        }
    }

    try {
        $requests = [System.Data.DataTable](Invoke-Sql -Query $QueryRequests)
        $schedulers = [System.Data.DataTable](Invoke-Sql -Query $QuerySchedulers)
        $waitsNow = [System.Data.DataTable](Invoke-Sql -Query $QueryWaitStats)
        $ioNow = [System.Data.DataTable](Invoke-Sql -Query $QueryIoStats)

        $block = New-Object System.Text.StringBuilder
        [void]$block.AppendLine("")
        [void]$block.AppendLine("--- loop #$loopIndex ---")
        [void]$block.AppendLine("timestamp_local: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')")
        [void]$block.AppendLine("actual_interval_since_prev_ms: $loopDelayMs")
        [void]$block.AppendLine("target_interval_ms: $($IntervalSeconds * 1000)")
        if ($delayNote) { [void]$block.AppendLine($delayNote.Trim()) }

        # Schedulers
        [void]$block.AppendLine("")
        [void]$block.AppendLine("[schedulers] sys.dm_os_schedulers (sum, scheduler_id < 255)")
        [void]$block.AppendLine((Format-TableForLog -Table $schedulers -MaxRows 5))

        # Active requests
        [void]$block.AppendLine("")
        [void]$block.AppendLine("[requests] sys.dm_exec_requests (active, excluding this session)")
        [void]$block.AppendLine((Format-TableForLog -Table $requests -MaxRows $TopRequests))

        # Delta waits
        [void]$block.AppendLine("")
        [void]$block.AppendLine("[waits] sys.dm_os_wait_stats DELTA since previous loop (benign filtered from TOP list)")
        if ($null -eq $prevWaits) {
            [void]$block.AppendLine("(first loop - baseline captured; deltas from loop #2)")
        } else {
            $waitDeltas = New-Object System.Collections.Generic.List[object]
            $prevMap = @{}
            foreach ($row in $prevWaits.Rows) {
                $prevMap[$row.wait_type] = $row
            }
            foreach ($row in $waitsNow.Rows) {
                $wt = [string]$row.wait_type
                $p = $prevMap[$wt]
                if ($null -eq $p) {
                    $dWait = [long]$row.wait_time_ms
                    $dTasks = [long]$row.waiting_tasks_count
                    $dSignal = [long]$row.signal_wait_time_ms
                } else {
                    $dWait = [long]$row.wait_time_ms - [long]$p.wait_time_ms
                    $dTasks = [long]$row.waiting_tasks_count - [long]$p.waiting_tasks_count
                    $dSignal = [long]$row.signal_wait_time_ms - [long]$p.signal_wait_time_ms
                }
                if ($dWait -lt 0) { $dWait = [long]$row.wait_time_ms }
                if ($dTasks -lt 0) { $dTasks = [long]$row.waiting_tasks_count }
                if ($dSignal -lt 0) { $dSignal = [long]$row.signal_wait_time_ms }
                if ($dWait -gt 0 -or $dTasks -gt 0) {
                    $waitDeltas.Add([pscustomobject]@{
                        wait_type           = $wt
                        delta_wait_ms       = $dWait
                        delta_wait_tasks    = $dTasks
                        delta_signal_ms     = $dSignal
                        benign              = (Test-BenignWait $wt)
                    })
                }
            }
            $topAll = @($waitDeltas | Sort-Object delta_wait_ms -Descending | Select-Object -First $TopWaits)
            $topFiltered = @($waitDeltas | Where-Object { -not $_.benign } | Sort-Object delta_wait_ms -Descending | Select-Object -First $TopWaits)

            [void]$block.AppendLine("TOP delta waits (excluding benign):")
            if ($topFiltered.Length -eq 0) {
                [void]$block.AppendLine("(no non-benign delta waits this interval)")
            } else {
                foreach ($w in $topFiltered) {
                    [void]$block.AppendLine(("  {0,-45} delta_ms={1,8} tasks={2,6} signal_ms={3,8}" -f $w.wait_type, $w.delta_wait_ms, $w.delta_wait_tasks, $w.delta_signal_ms))
                }
            }
            [void]$block.AppendLine("TOP delta waits (all, including benign):")
            foreach ($w in $topAll) {
                $tag = if ($w.benign) { "[benign]" } else { "" }
                [void]$block.AppendLine(("  {0,-45} delta_ms={1,8} tasks={2,6} signal_ms={3,8} {4}" -f $w.wait_type, $w.delta_wait_ms, $w.delta_wait_tasks, $w.delta_signal_ms, $tag))
            }
        }

        # Delta IO
        [void]$block.AppendLine("")
        [void]$block.AppendLine("[io] sys.dm_io_virtual_file_stats DELTA stall (read/write/total ms) since previous loop")
        if ($null -eq $prevIo) {
            [void]$block.AppendLine("(first loop - baseline captured; deltas from loop #2)")
        } else {
            $ioDeltas = New-Object System.Collections.Generic.List[object]
            $prevIoMap = @{}
            foreach ($row in $prevIo.Rows) {
                $key = "{0}:{1}" -f $row.database_id, $row.file_id
                $prevIoMap[$key] = $row
            }
            foreach ($row in $ioNow.Rows) {
                $key = "{0}:{1}" -f $row.database_id, $row.file_id
                $p = $prevIoMap[$key]
                if ($null -eq $p) { continue }
                $dRead = [long]$row.io_stall_read_ms - [long]$p.io_stall_read_ms
                $dWrite = [long]$row.io_stall_write_ms - [long]$p.io_stall_write_ms
                $dTotal = [long]$row.io_stall_total_ms - [long]$p.io_stall_total_ms
                if ($dRead -lt 0) { $dRead = 0 }; if ($dWrite -lt 0) { $dWrite = 0 }; if ($dTotal -lt 0) { $dTotal = 0 }
                if ($dRead -gt 0 -or $dWrite -gt 0 -or $dTotal -gt 0) {
                    $ioDeltas.Add([pscustomobject]@{
                        database_id     = $row.database_id
                        file_id         = $row.file_id
                        delta_read_stall_ms  = $dRead
                        delta_write_stall_ms = $dWrite
                        delta_total_stall_ms = $dTotal
                    })
                }
            }
            $topIo = @($ioDeltas | Sort-Object delta_total_stall_ms -Descending | Select-Object -First 10)
            if ($topIo.Length -eq 0) {
                [void]$block.AppendLine("(no IO stall delta this interval)")
            } else {
                foreach ($i in $topIo) {
                    [void]$block.AppendLine(("  db={0,3} file={1,2}  read_stall_delta={2,8}ms  write_stall_delta={3,8}ms  total_stall_delta={4,8}ms" -f `
                        $i.database_id, $i.file_id, $i.delta_read_stall_ms, $i.delta_write_stall_ms, $i.delta_total_stall_ms))
                }
            }
        }

        $swLoop.Stop()
        [void]$block.AppendLine("")
        [void]$block.AppendLine("loop_body_elapsed_ms: $($swLoop.ElapsedMilliseconds) (SQL query time for this sample)")

        Write-LogBlock -Text $block.ToString()

        # Console hint on skew
        if ($delayNote) {
            Write-Host "[loop #$loopIndex] interval ${loopDelayMs}ms (skew!)$delayNote" -ForegroundColor Yellow
        } elseif ($loopIndex % 10 -eq 0) {
            Write-Host "[loop #$loopIndex] OK interval ${loopDelayMs}ms" -ForegroundColor DarkGray
        }

        $prevWaits = $waitsNow
        $prevIo = $ioNow
    }
    catch {
        $err = @"
--- loop #$loopIndex ERROR ---
timestamp_local: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')
actual_interval_since_prev_ms: $loopDelayMs
error: $($_.Exception.Message)
"@
        Write-LogBlock -Text $err
        Write-Host $err -ForegroundColor Red
    }

    # Sleep remainder of interval (after work), so target period ≈ IntervalSeconds
    $workMs = $swLoop.ElapsedMilliseconds
    $sleepMs = ($IntervalSeconds * 1000) - $workMs
    if ($sleepMs -gt 0) {
        Start-Sleep -Milliseconds $sleepMs
    }
}
