using System.ComponentModel;
using ModelContextProtocol.Server;
using MemoryBank.Core.Configuration;
using MemoryBank.Core.Embeddings;
using MemoryBank.Core.Models;
using MemoryBank.Core.Storage;

namespace MemoryBank.Server.Tools;

[McpServerToolType]
public class HealthTool
{
    private readonly MemoryBankDb _db;
    private readonly MemoryStore _store;
    private readonly BackupService _backup;
    private readonly OnnxEmbeddingService _embeddings;
    private readonly MemoryBankConfiguration _config;

    public HealthTool(MemoryBankDb db, MemoryStore store, BackupService backup,
        OnnxEmbeddingService embeddings, MemoryBankConfiguration config)
    {
        _db = db;
        _store = store;
        _backup = backup;
        _embeddings = embeddings;
        _config = config;
    }

    [McpServerTool(Name = "health_check"), Description("Check the health of the MemoryBank database, indexes, and embedding model.")]
    public string HealthCheck()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var integrity = _db.RunIntegrityCheck();
        var stats = _store.GetStats();
        var dbPath = _config.Database.Path;
        var dbSize = File.Exists(dbPath) ? new FileInfo(dbPath).Length : 0;

        sw.Stop();

        return ToolResponse<object>.Ok(new
        {
            status = integrity == "ok" ? "healthy" : "degraded",
            dbFilePath = dbPath,
            dbFileSize = FormatSize(dbSize),
            totalMemories = stats["totalMemories"],
            totalChunks = stats["totalChunks"],
            totalRevisions = stats["totalRevisions"],
            totalCategories = stats["totalCategories"],
            totalTags = stats["totalTags"],
            totalEmbeddings = stats["totalEmbeddings"],
            integrityCheck = integrity,
            ftsStatus = "ok",
            embeddingModel = _config.Embedding.ModelName,
            embeddingStatus = _embeddings.IsAvailable ? "ok" : "unavailable",
            pendingReembeddings = 0,
            lastBackup = _backup.GetLastBackupTime(),
            schemaVersion = 1,
            checkDurationMs = sw.ElapsedMilliseconds
        }).ToJson();
    }

    [McpServerTool(Name = "backup"), Description("Create a backup of the database.")]
    public string Backup()
    {
        try
        {
            var path = _backup.CreateBackup();
            return ToolResponse<object>.Ok(new { backupPath = path }).ToJson();
        }
        catch (Exception ex)
        {
            return ToolResponse<object>.Fail(ErrorCodes.BackupFailed, ex.Message).ToJson();
        }
    }

    [McpServerTool(Name = "restore_backup"), Description("Restore the database from a backup file.")]
    public string RestoreBackup([Description("Path to backup file")] string path)
    {
        try
        {
            _backup.RestoreBackup(path);
            return ToolResponse<object>.Ok(new { restored = true, fromPath = path }).ToJson();
        }
        catch (Exception ex)
        {
            return ToolResponse<object>.Fail(ErrorCodes.RestoreFailed, ex.Message).ToJson();
        }
    }

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
        };
    }
}
