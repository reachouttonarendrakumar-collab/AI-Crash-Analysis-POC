using Microsoft.Data.Sqlite;

namespace CrashCollector.Console.Data;

/// <summary>
/// Repository for the AIAnalysis and AIFixes tables.
/// </summary>
public sealed class AIRepository
{
    private readonly CrashDb _db;

    public AIRepository(CrashDb db) => _db = db;

    // =========================================================================
    //  AIAnalysis CRUD
    // =========================================================================

    public void UpsertAnalysis(AIAnalysisRow row)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AIAnalysis
                (AnalysisId, BucketId, RootCause, Confidence, SuggestedFix,
                 AffectedFile, AffectedFunction, Status, ErrorMessage,
                 PromptTokens, ResponseTokens, CompletedAtUtc)
            VALUES
                (@id, @bucketId, @rootCause, @confidence, @suggestedFix,
                 @affectedFile, @affectedFunction, @status, @errorMessage,
                 @promptTokens, @responseTokens, @completedAt)
            ON CONFLICT(AnalysisId) DO UPDATE SET
                RootCause       = excluded.RootCause,
                Confidence      = excluded.Confidence,
                SuggestedFix    = excluded.SuggestedFix,
                AffectedFile    = excluded.AffectedFile,
                AffectedFunction = excluded.AffectedFunction,
                Status          = excluded.Status,
                ErrorMessage    = excluded.ErrorMessage,
                PromptTokens    = excluded.PromptTokens,
                ResponseTokens  = excluded.ResponseTokens,
                CompletedAtUtc  = excluded.CompletedAtUtc;
            """;

        cmd.Parameters.AddWithValue("@id", row.AnalysisId);
        cmd.Parameters.AddWithValue("@bucketId", row.BucketId);
        cmd.Parameters.AddWithValue("@rootCause", (object?)row.RootCause ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@confidence", row.Confidence);
        cmd.Parameters.AddWithValue("@suggestedFix", (object?)row.SuggestedFix ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@affectedFile", (object?)row.AffectedFile ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@affectedFunction", (object?)row.AffectedFunction ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", row.Status);
        cmd.Parameters.AddWithValue("@errorMessage", (object?)row.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@promptTokens", row.PromptTokens);
        cmd.Parameters.AddWithValue("@responseTokens", row.ResponseTokens);
        cmd.Parameters.AddWithValue("@completedAt", (object?)row.CompletedAtUtc ?? DBNull.Value);

        cmd.ExecuteNonQuery();
    }

    public List<AIAnalysisRow> GetAllAnalyses(int limit = 100)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM AIAnalysis ORDER BY CreatedAtUtc DESC LIMIT @limit;";
        cmd.Parameters.AddWithValue("@limit", limit);

        var rows = new List<AIAnalysisRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) rows.Add(ReadAnalysisRow(r));
        return rows;
    }

    /// <summary>Returns only the most recent analysis per bucket.</summary>
    public List<AIAnalysisRow> GetLatestAnalysesPerBucket(int limit = 100)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT a.* FROM AIAnalysis a
            INNER JOIN (
                SELECT BucketId, MAX(CreatedAtUtc) AS MaxCreated
                FROM AIAnalysis GROUP BY BucketId
            ) latest ON a.BucketId = latest.BucketId AND a.CreatedAtUtc = latest.MaxCreated
            ORDER BY a.CreatedAtUtc DESC LIMIT @limit;
            """;
        cmd.Parameters.AddWithValue("@limit", limit);

        var rows = new List<AIAnalysisRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) rows.Add(ReadAnalysisRow(r));
        return rows;
    }

    public AIAnalysisRow? GetAnalysisByBucket(string bucketId)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM AIAnalysis WHERE BucketId = @id ORDER BY CreatedAtUtc DESC LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", bucketId);

        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadAnalysisRow(r) : null;
    }

    public AIAnalysisRow? GetAnalysisById(string analysisId)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM AIAnalysis WHERE AnalysisId = @id;";
        cmd.Parameters.AddWithValue("@id", analysisId);

        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadAnalysisRow(r) : null;
    }

    // =========================================================================
    //  AIFixes CRUD
    // =========================================================================

    public void UpsertFix(AIFixRow row)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AIFixes
                (FixId, AnalysisId, BucketId, BranchName, CommitSha,
                 PRNumber, PRUrl, PRTitle, PRStatus, FixDescription,
                 FilesChanged, ErrorMessage, UpdatedAtUtc)
            VALUES
                (@id, @analysisId, @bucketId, @branchName, @commitSha,
                 @prNumber, @prUrl, @prTitle, @prStatus, @fixDescription,
                 @filesChanged, @errorMessage, datetime('now'))
            ON CONFLICT(FixId) DO UPDATE SET
                BranchName      = excluded.BranchName,
                CommitSha       = excluded.CommitSha,
                PRNumber        = excluded.PRNumber,
                PRUrl           = excluded.PRUrl,
                PRTitle         = excluded.PRTitle,
                PRStatus        = excluded.PRStatus,
                FixDescription  = excluded.FixDescription,
                FilesChanged    = excluded.FilesChanged,
                ErrorMessage    = excluded.ErrorMessage,
                UpdatedAtUtc    = datetime('now');
            """;

        cmd.Parameters.AddWithValue("@id", row.FixId);
        cmd.Parameters.AddWithValue("@analysisId", row.AnalysisId);
        cmd.Parameters.AddWithValue("@bucketId", row.BucketId);
        cmd.Parameters.AddWithValue("@branchName", (object?)row.BranchName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@commitSha", (object?)row.CommitSha ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@prNumber", row.PRNumber > 0 ? row.PRNumber : DBNull.Value);
        cmd.Parameters.AddWithValue("@prUrl", (object?)row.PRUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@prTitle", (object?)row.PRTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@prStatus", row.PRStatus);
        cmd.Parameters.AddWithValue("@fixDescription", (object?)row.FixDescription ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@filesChanged", (object?)row.FilesChanged ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@errorMessage", (object?)row.ErrorMessage ?? DBNull.Value);

        cmd.ExecuteNonQuery();
    }

    public List<AIFixRow> GetAllFixes(int limit = 100)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM AIFixes ORDER BY CreatedAtUtc DESC LIMIT @limit;";
        cmd.Parameters.AddWithValue("@limit", limit);

        var rows = new List<AIFixRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) rows.Add(ReadFixRow(r));
        return rows;
    }

    /// <summary>Returns only the most recent fix per bucket.</summary>
    public List<AIFixRow> GetLatestFixesPerBucket(int limit = 100)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT f.* FROM AIFixes f
            INNER JOIN (
                SELECT BucketId, MAX(CreatedAtUtc) AS MaxCreated
                FROM AIFixes GROUP BY BucketId
            ) latest ON f.BucketId = latest.BucketId AND f.CreatedAtUtc = latest.MaxCreated
            ORDER BY f.CreatedAtUtc DESC LIMIT @limit;
            """;
        cmd.Parameters.AddWithValue("@limit", limit);

        var rows = new List<AIFixRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) rows.Add(ReadFixRow(r));
        return rows;
    }

    /// <summary>Deletes all but the latest analysis and fix per bucket.
    /// Fixes are deleted first to satisfy foreign key constraints.</summary>
    public int PurgeStaleRecords()
    {
        int deleted = 0;
        // Delete stale fixes FIRST (FK references AIAnalysis)
        using (var cmd = _db.Connection.CreateCommand())
        {
            cmd.CommandText = """
                DELETE FROM AIFixes WHERE FixId NOT IN (
                    SELECT FixId FROM AIFixes f
                    INNER JOIN (
                        SELECT BucketId, MAX(CreatedAtUtc) AS MaxCreated
                        FROM AIFixes GROUP BY BucketId
                    ) latest ON f.BucketId = latest.BucketId AND f.CreatedAtUtc = latest.MaxCreated
                );
                """;
            deleted += cmd.ExecuteNonQuery();
        }
        // Then delete stale analyses
        using (var cmd = _db.Connection.CreateCommand())
        {
            cmd.CommandText = """
                DELETE FROM AIAnalysis WHERE AnalysisId NOT IN (
                    SELECT AnalysisId FROM AIAnalysis a
                    INNER JOIN (
                        SELECT BucketId, MAX(CreatedAtUtc) AS MaxCreated
                        FROM AIAnalysis GROUP BY BucketId
                    ) latest ON a.BucketId = latest.BucketId AND a.CreatedAtUtc = latest.MaxCreated
                );
                """;
            deleted += cmd.ExecuteNonQuery();
        }
        return deleted;
    }

    public AIFixRow? GetFixByBucket(string bucketId)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM AIFixes WHERE BucketId = @id ORDER BY CreatedAtUtc DESC LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", bucketId);

        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadFixRow(r) : null;
    }

    public AIFixRow? GetFixById(string fixId)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM AIFixes WHERE FixId = @id;";
        cmd.Parameters.AddWithValue("@id", fixId);

        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadFixRow(r) : null;
    }

    // =========================================================================
    //  Row mappers
    // =========================================================================

    private static AIAnalysisRow ReadAnalysisRow(SqliteDataReader r) => new()
    {
        AnalysisId = r.GetString(r.GetOrdinal("AnalysisId")),
        BucketId = r.GetString(r.GetOrdinal("BucketId")),
        RootCause = r.IsDBNull(r.GetOrdinal("RootCause")) ? null : r.GetString(r.GetOrdinal("RootCause")),
        Confidence = r.GetDouble(r.GetOrdinal("Confidence")),
        SuggestedFix = r.IsDBNull(r.GetOrdinal("SuggestedFix")) ? null : r.GetString(r.GetOrdinal("SuggestedFix")),
        AffectedFile = r.IsDBNull(r.GetOrdinal("AffectedFile")) ? null : r.GetString(r.GetOrdinal("AffectedFile")),
        AffectedFunction = r.IsDBNull(r.GetOrdinal("AffectedFunction")) ? null : r.GetString(r.GetOrdinal("AffectedFunction")),
        Status = r.GetString(r.GetOrdinal("Status")),
        ErrorMessage = r.IsDBNull(r.GetOrdinal("ErrorMessage")) ? null : r.GetString(r.GetOrdinal("ErrorMessage")),
        PromptTokens = r.GetInt32(r.GetOrdinal("PromptTokens")),
        ResponseTokens = r.GetInt32(r.GetOrdinal("ResponseTokens")),
        CreatedAtUtc = r.GetString(r.GetOrdinal("CreatedAtUtc")),
        CompletedAtUtc = r.IsDBNull(r.GetOrdinal("CompletedAtUtc")) ? null : r.GetString(r.GetOrdinal("CompletedAtUtc")),
    };

    private static AIFixRow ReadFixRow(SqliteDataReader r) => new()
    {
        FixId = r.GetString(r.GetOrdinal("FixId")),
        AnalysisId = r.GetString(r.GetOrdinal("AnalysisId")),
        BucketId = r.GetString(r.GetOrdinal("BucketId")),
        BranchName = r.IsDBNull(r.GetOrdinal("BranchName")) ? null : r.GetString(r.GetOrdinal("BranchName")),
        CommitSha = r.IsDBNull(r.GetOrdinal("CommitSha")) ? null : r.GetString(r.GetOrdinal("CommitSha")),
        PRNumber = r.IsDBNull(r.GetOrdinal("PRNumber")) ? 0 : r.GetInt32(r.GetOrdinal("PRNumber")),
        PRUrl = r.IsDBNull(r.GetOrdinal("PRUrl")) ? null : r.GetString(r.GetOrdinal("PRUrl")),
        PRTitle = r.IsDBNull(r.GetOrdinal("PRTitle")) ? null : r.GetString(r.GetOrdinal("PRTitle")),
        PRStatus = r.GetString(r.GetOrdinal("PRStatus")),
        FixDescription = r.IsDBNull(r.GetOrdinal("FixDescription")) ? null : r.GetString(r.GetOrdinal("FixDescription")),
        FilesChanged = r.IsDBNull(r.GetOrdinal("FilesChanged")) ? null : r.GetString(r.GetOrdinal("FilesChanged")),
        ErrorMessage = r.IsDBNull(r.GetOrdinal("ErrorMessage")) ? null : r.GetString(r.GetOrdinal("ErrorMessage")),
        CreatedAtUtc = r.GetString(r.GetOrdinal("CreatedAtUtc")),
        UpdatedAtUtc = r.GetString(r.GetOrdinal("UpdatedAtUtc")),
    };
}

// =========================================================================
//  Row models
// =========================================================================

public sealed class AIAnalysisRow
{
    public string AnalysisId { get; set; } = string.Empty;
    public string BucketId { get; set; } = string.Empty;
    public string? RootCause { get; set; }
    public double Confidence { get; set; }
    public string? SuggestedFix { get; set; }
    public string? AffectedFile { get; set; }
    public string? AffectedFunction { get; set; }
    public string Status { get; set; } = "Pending";
    public string? ErrorMessage { get; set; }
    public int PromptTokens { get; set; }
    public int ResponseTokens { get; set; }
    public string CreatedAtUtc { get; set; } = string.Empty;
    public string? CompletedAtUtc { get; set; }
}

public sealed class AIFixRow
{
    public string FixId { get; set; } = string.Empty;
    public string AnalysisId { get; set; } = string.Empty;
    public string BucketId { get; set; } = string.Empty;
    public string? BranchName { get; set; }
    public string? CommitSha { get; set; }
    public int PRNumber { get; set; }
    public string? PRUrl { get; set; }
    public string? PRTitle { get; set; }
    public string PRStatus { get; set; } = "Pending";
    public string? FixDescription { get; set; }
    public string? FilesChanged { get; set; }
    public string? ErrorMessage { get; set; }
    public string CreatedAtUtc { get; set; } = string.Empty;
    public string UpdatedAtUtc { get; set; } = string.Empty;
}
