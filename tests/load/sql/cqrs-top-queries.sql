/*
  CQRS load test Query Store / DMV ozeti.
  Secret icermez. sqlcmd ile calistirin:

  sqlcmd -S localhost -d VetinityCommandDb_LoadTest -i tests/load/sql/cqrs-top-queries.sql -v DatabaseName=VetinityCommandDb_LoadTest
  sqlcmd -S localhost -d VetinityQueryDb_LoadTest -i tests/load/sql/cqrs-top-queries.sql -v DatabaseName=VetinityQueryDb_LoadTest
*/

SET NOCOUNT ON;

DECLARE @DatabaseName sysname = N'$(DatabaseName)';
DECLARE @TopN int = 25;

IF DB_NAME() <> @DatabaseName
BEGIN
    DECLARE @actualDb sysname = DB_NAME();
    RAISERROR(N'Connected database does not match DatabaseName variable.', 16, 1);
    RETURN;
END;

PRINT 'Database: ' + @DatabaseName;
PRINT 'CollectedAtUtc: ' + CONVERT(varchar(33), SYSUTCDATETIME(), 127);

IF EXISTS (SELECT 1 FROM sys.database_query_store_options WHERE actual_state_desc IN ('READ_WRITE', 'READ_ONLY'))
BEGIN
    SELECT TOP (@TopN)
        q.query_id,
        p.plan_id,
        LEFT(qt.query_sql_text, 4000) AS query_sql_text,
        rs.count_executions AS execution_count,
        CAST(rs.avg_cpu_time / 1000.0 AS decimal(18, 2)) AS avg_cpu_ms,
        CAST(rs.max_cpu_time / 1000.0 AS decimal(18, 2)) AS max_cpu_ms,
        rs.avg_logical_io_reads AS avg_logical_reads,
        rs.max_logical_io_reads AS max_logical_reads,
        CAST(rs.avg_duration / 1000.0 AS decimal(18, 2)) AS avg_duration_ms,
        CAST(rs.max_duration / 1000.0 AS decimal(18, 2)) AS max_duration_ms,
        CAST(rs.avg_cpu_time * rs.count_executions / 1000.0 AS decimal(18, 2)) AS total_cpu_ms
    FROM sys.query_store_query q
    INNER JOIN sys.query_store_query_text qt ON q.query_text_id = qt.query_text_id
    INNER JOIN sys.query_store_plan p ON q.query_id = p.query_id
    INNER JOIN sys.query_store_runtime_stats rs ON p.plan_id = rs.plan_id
    ORDER BY total_cpu_ms DESC;
END
ELSE
BEGIN
    PRINT 'Query Store disabled; using sys.dm_exec_query_stats fallback.';

    SELECT TOP (@TopN)
        SUBSTRING(st.text, (qs.statement_start_offset / 2) + 1,
            CASE
                WHEN qs.statement_end_offset = -1 THEN LEN(CONVERT(nvarchar(max), st.text)) * 2
                ELSE qs.statement_end_offset - qs.statement_start_offset
            END) AS query_sql_text,
        qs.execution_count,
        CAST(qs.total_worker_time / 1000.0 AS decimal(18, 2)) AS total_cpu_ms,
        CAST(qs.total_logical_reads AS bigint) AS total_logical_reads,
        CAST(qs.total_elapsed_time / qs.execution_count / 1000.0 AS decimal(18, 2)) AS avg_duration_ms,
        CAST(qs.max_elapsed_time / 1000.0 AS decimal(18, 2)) AS max_duration_ms
    FROM sys.dm_exec_query_stats qs
    CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
    WHERE DB_NAME(st.dbid) = @DatabaseName
       OR st.dbid IS NULL
    ORDER BY qs.total_worker_time DESC;
END;
