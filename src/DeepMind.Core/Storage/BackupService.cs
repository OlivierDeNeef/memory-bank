using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using DeepMind.Core.Configuration;

namespace DeepMind.Core.Storage;

public class BackupService
{
    private readonly DeepMindConfiguration _config;
    private readonly ILogger<BackupService> _logger;

    public BackupService(DeepMindConfiguration config, ILogger<BackupService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public string CreateBackup()
    {
        Directory.CreateDirectory(_config.Backup.Path);

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmssZ");
        var backupPath = Path.Combine(_config.Backup.Path, $"deepmind_backup_{timestamp}.db");

        using var source = new SqliteConnection($"Data Source={_config.Database.Path}");
        source.Open();

        using var destination = new SqliteConnection($"Data Source={backupPath}");
        destination.Open();

        source.BackupDatabase(destination);

        _logger.LogInformation("Backup created at {BackupPath}", backupPath);

        EnforceRetention();
        return backupPath;
    }

    public void RestoreBackup(string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup file not found", backupPath);

        // Verify backup integrity
        using (var verify = new SqliteConnection($"Data Source={backupPath};Mode=ReadOnly"))
        {
            verify.Open();
            using var cmd = verify.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            var result = cmd.ExecuteScalar()?.ToString();
            if (result != "ok")
                throw new InvalidOperationException($"Backup integrity check failed: {result}");
        }

        File.Copy(backupPath, _config.Database.Path, overwrite: true);
        _logger.LogInformation("Database restored from {BackupPath}", backupPath);
    }

    public string? GetLastBackupTime()
    {
        if (!Directory.Exists(_config.Backup.Path)) return null;

        var latest = Directory.GetFiles(_config.Backup.Path, "deepmind_backup_*.db")
            .OrderByDescending(f => f)
            .FirstOrDefault();

        if (latest == null) return null;

        return File.GetCreationTimeUtc(latest).ToString("o");
    }

    public bool IsBackupDue()
    {
        if (!_config.Backup.AutoBackupEnabled) return false;

        var lastBackup = GetLastBackupTime();
        if (lastBackup == null) return true;

        var lastTime = DateTime.Parse(lastBackup);
        return (DateTime.UtcNow - lastTime).TotalHours >= _config.Backup.AutoBackupIntervalHours;
    }

    private void EnforceRetention()
    {
        if (!Directory.Exists(_config.Backup.Path)) return;

        var backups = Directory.GetFiles(_config.Backup.Path, "deepmind_backup_*.db")
            .OrderByDescending(f => f)
            .ToList();

        while (backups.Count > _config.Backup.MaxBackups)
        {
            var oldest = backups.Last();
            File.Delete(oldest);
            backups.RemoveAt(backups.Count - 1);
            _logger.LogInformation("Deleted old backup: {BackupPath}", oldest);
        }
    }
}
