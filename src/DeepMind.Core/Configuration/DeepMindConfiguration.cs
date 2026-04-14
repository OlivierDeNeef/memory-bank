namespace DeepMind.Core.Configuration;

public class DeepMindConfiguration
{
    public DatabaseConfig Database { get; set; } = new();
    public BackupConfig Backup { get; set; } = new();
    public EmbeddingConfig Embedding { get; set; } = new();
    public SearchConfig Search { get; set; } = new();
    public ValidationConfig Validation { get; set; } = new();
    public MemoryDefaults Memory { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();
}

public class DatabaseConfig
{
    public string Path { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".deepmind", "deepmind.db");
    public bool WalMode { get; set; } = true;
    public int BusyTimeout { get; set; } = 5000;
}

public class BackupConfig
{
    public string Path { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".deepmind", "backups");
    public int MaxBackups { get; set; } = 10;
    public bool AutoBackupEnabled { get; set; } = true;
    public int AutoBackupIntervalHours { get; set; } = 24;
}

public class EmbeddingConfig
{
    public string ModelPath { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".deepmind", "models", "all-MiniLM-L6-v2.onnx");
    public string ModelName { get; set; } = "all-MiniLM-L6-v2";
    public int Dimensions { get; set; } = 384;
    public int MaxTokensPerChunk { get; set; } = 400;
    public int ChunkOverlapTokens { get; set; } = 50;
}

public class SearchConfig
{
    public int DefaultLimit { get; set; } = 10;
    public int MaxLimit { get; set; } = 100;
    public float VectorWeight { get; set; } = 0.4f;
    public float KeywordWeight { get; set; } = 0.35f;
    public float PriorityWeight { get; set; } = 0.25f;
    public float PinBonus { get; set; } = 0.5f;
    public float RecencyDecayPerDay { get; set; } = 0.5f;
    public float AccessBoostFactor { get; set; } = 2f;
}

public class ValidationConfig
{
    public int MaxContentLength { get; set; } = 100_000;
    public int MaxMetadataSize { get; set; } = 10_240;
    public int MaxTagsPerMemory { get; set; } = 50;
    public int MaxCategoryDepth { get; set; } = 10;
    public int MaxTagLength { get; set; } = 100;
    public int MaxCategoryNameLength { get; set; } = 200;
}

public class MemoryDefaults
{
    public int DefaultPriority { get; set; } = 3;
    public string DefaultType { get; set; } = "fact";
    public float DuplicateThreshold { get; set; } = 0.90f;
}

public class LoggingConfig
{
    public string Level { get; set; } = "Information";
    public string FilePath { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".deepmind", "logs", "deepmind.log");
    public int MaxFileSizeMb { get; set; } = 50;
    public int MaxRetainedFiles { get; set; } = 5;
}
