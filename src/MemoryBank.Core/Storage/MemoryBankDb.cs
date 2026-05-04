using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using MemoryBank.Core.Configuration;

namespace MemoryBank.Core.Storage;

public class MemoryBankDb : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ILogger<MemoryBankDb> _logger;
    private readonly MemoryBankConfiguration _config;

    public MemoryBankDb(MemoryBankConfiguration config, ILogger<MemoryBankDb> logger)
    {
        _config = config;
        _logger = logger;

        var dir = Path.GetDirectoryName(config.Database.Path)!;
        Directory.CreateDirectory(dir);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = config.Database.Path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        ConfigurePragmas();
        RunMigrations();
    }

    public SqliteConnection Connection => _connection;

    private void ConfigurePragmas()
    {
        using var cmd = _connection.CreateCommand();
        if (_config.Database.WalMode)
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
        }

        cmd.CommandText = $"PRAGMA busy_timeout={_config.Database.BusyTimeout};";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();

        _logger.LogInformation("Database pragmas configured (WAL={WalMode}, BusyTimeout={BusyTimeout})",
            _config.Database.WalMode, _config.Database.BusyTimeout);
    }

    private void RunMigrations()
    {
        EnsureSchemaVersionTable();
        var currentVersion = GetSchemaVersion();

        var pending = Migrations.All
            .Where(m => m.Version > currentVersion)
            .OrderBy(m => m.Version)
            .ToList();
        if (pending.Count == 0) return;

        // Column/CHECK migrations rebuild tables via DROP + INSERT + RENAME. With FKs
        // enforced, dropping `memories` would cascade-delete chunks/embeddings/revisions/
        // memory_tags/memory_links. PRAGMA foreign_keys is a no-op inside a transaction,
        // so it must be toggled outside the per-migration transaction.
        SetForeignKeys(false);
        try
        {
            foreach (var migration in pending)
            {
                _logger.LogInformation("Applying migration V{Version}: {Description}", migration.Version, migration.Description);

                using var transaction = _connection.BeginTransaction();
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = migration.Sql;
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = "INSERT INTO schema_version (version, applied_at, description) VALUES (@v, @t, @d)";
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("@v", migration.Version);
                    cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("@d", migration.Description);
                    cmd.ExecuteNonQuery();

                    transaction.Commit();
                    _logger.LogInformation("Migration V{Version} applied successfully", migration.Version);
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }

            AssertForeignKeyIntegrity();
        }
        finally
        {
            SetForeignKeys(true);
        }
    }

    private void SetForeignKeys(bool enabled)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA foreign_keys={(enabled ? "ON" : "OFF")};";
        cmd.ExecuteNonQuery();
    }

    private void AssertForeignKeyIntegrity()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_key_check;";
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var table = reader.GetString(0);
            var rowid = reader.IsDBNull(1) ? "null" : reader.GetValue(1).ToString();
            throw new InvalidOperationException(
                $"Foreign key integrity violation after migrations (table={table}, rowid={rowid}). Aborting startup.");
        }
    }

    private void EnsureSchemaVersionTable()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_version (
                version     INTEGER NOT NULL,
                applied_at  TEXT NOT NULL,
                description TEXT
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private int GetSchemaVersion()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public int CurrentSchemaVersion => GetSchemaVersion();

    public SqliteCommand CreateCommand(string sql, SqliteTransaction? transaction = null)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        if (transaction != null)
            cmd.Transaction = transaction;
        return cmd;
    }

    public SqliteTransaction BeginTransaction() => _connection.BeginTransaction();

    public string RunIntegrityCheck()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        return cmd.ExecuteScalar()?.ToString() ?? "unknown";
    }

    public void Checkpoint()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        cmd.ExecuteNonQuery();
    }

    public void Vacuum()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "VACUUM;";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try { Checkpoint(); } catch { /* best effort */ }
        _connection.Close();
        _connection.Dispose();
    }
}
