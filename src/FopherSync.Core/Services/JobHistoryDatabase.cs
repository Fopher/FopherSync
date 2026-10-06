using Microsoft.Data.Sqlite;
using FopherSync.Core.Models;

namespace FopherSync.Core.Services;

public class JobHistoryDatabase
{
    private readonly string _connectionString;

    public JobHistoryDatabase(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS RunHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                JobId TEXT NOT NULL,
                JobName TEXT NOT NULL,
                StartTime TEXT NOT NULL,
                EndTime TEXT NOT NULL,
                DurationSeconds REAL NOT NULL,
                Status INTEGER NOT NULL,
                ExitCode INTEGER NOT NULL,
                ExitSummary TEXT NOT NULL,
                FilesCopied INTEGER NOT NULL,
                FilesTotal INTEGER NOT NULL,
                BytesTransferred INTEGER NOT NULL,
                ErrorCount INTEGER NOT NULL,
                WasDryRun INTEGER NOT NULL,
                LogFilePath TEXT,
                ErrorDetails TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_runhistory_jobid ON RunHistory(JobId);
            CREATE INDEX IF NOT EXISTS idx_runhistory_starttime ON RunHistory(StartTime DESC);
        """;
        cmd.ExecuteNonQuery();
    }

    public async Task InsertRunRecordAsync(JobRunRecord record)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO RunHistory (
                JobId, JobName, StartTime, EndTime, DurationSeconds,
                Status, ExitCode, ExitSummary, FilesCopied, FilesTotal,
                BytesTransferred, ErrorCount, WasDryRun, LogFilePath, ErrorDetails
            ) VALUES (
                $JobId, $JobName, $StartTime, $EndTime, $DurationSeconds,
                $Status, $ExitCode, $ExitSummary, $FilesCopied, $FilesTotal,
                $BytesTransferred, $ErrorCount, $WasDryRun, $LogFilePath, $ErrorDetails
            );
        """;

        cmd.Parameters.AddWithValue("$JobId", record.JobId);
        cmd.Parameters.AddWithValue("$JobName", record.JobName);
        cmd.Parameters.AddWithValue("$StartTime", record.StartTime.ToString("o"));
        cmd.Parameters.AddWithValue("$EndTime", record.EndTime.ToString("o"));
        cmd.Parameters.AddWithValue("$DurationSeconds", record.DurationSeconds);
        cmd.Parameters.AddWithValue("$Status", (int)record.Status);
        cmd.Parameters.AddWithValue("$ExitCode", record.ExitCode);
        cmd.Parameters.AddWithValue("$ExitSummary", record.ExitSummary);
        cmd.Parameters.AddWithValue("$FilesCopied", record.FilesCopied);
        cmd.Parameters.AddWithValue("$FilesTotal", record.FilesTotal);
        cmd.Parameters.AddWithValue("$BytesTransferred", record.BytesTransferred);
        cmd.Parameters.AddWithValue("$ErrorCount", record.ErrorCount);
        cmd.Parameters.AddWithValue("$WasDryRun", record.WasDryRun ? 1 : 0);
        cmd.Parameters.AddWithValue("$LogFilePath", (object?)record.LogFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ErrorDetails", (object?)record.ErrorDetails ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<JobRunRecord>> GetRecentRunsAsync(int limit = 50, string? jobId = null)
    {
        return await GetRunsFilteredAsync(since: null, jobId: jobId, limit: limit);
    }

    public async Task<List<JobRunRecord>> GetRunsFilteredAsync(DateTime? since = null, string? jobId = null, int limit = 200)
    {
        var results = new List<JobRunRecord>();

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        var conditions = new List<string>();

        if (!string.IsNullOrEmpty(jobId))
        {
            conditions.Add("JobId = $JobId");
            cmd.Parameters.AddWithValue("$JobId", jobId);
        }

        if (since.HasValue)
        {
            conditions.Add("StartTime >= $Since");
            cmd.Parameters.AddWithValue("$Since", since.Value.ToString("o"));
        }

        var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        cmd.CommandText = $"""
            SELECT Id, JobId, JobName, StartTime, EndTime, DurationSeconds,
                   Status, ExitCode, ExitSummary, FilesCopied, FilesTotal,
                   BytesTransferred, ErrorCount, WasDryRun, LogFilePath, ErrorDetails
            FROM RunHistory
            {whereClause}
            ORDER BY StartTime DESC
            LIMIT $Limit;
        """;

        cmd.Parameters.AddWithValue("$Limit", limit);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new JobRunRecord
            {
                Id = reader.GetInt64(0),
                JobId = reader.GetString(1),
                JobName = reader.GetString(2),
                StartTime = DateTime.Parse(reader.GetString(3)),
                EndTime = DateTime.Parse(reader.GetString(4)),
                DurationSeconds = reader.GetDouble(5),
                Status = (JobStatus)reader.GetInt32(6),
                ExitCode = reader.GetInt32(7),
                ExitSummary = reader.GetString(8),
                FilesCopied = reader.GetInt64(9),
                FilesTotal = reader.GetInt64(10),
                BytesTransferred = reader.GetInt64(11),
                ErrorCount = reader.GetInt64(12),
                WasDryRun = reader.GetInt32(13) == 1,
                LogFilePath = reader.IsDBNull(14) ? string.Empty : reader.GetString(14),
                ErrorDetails = reader.IsDBNull(15) ? string.Empty : reader.GetString(15)
            });
        }

        return results;
    }
}
