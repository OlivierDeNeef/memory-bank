using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepMind.Core.Models;

public class ToolResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public ErrorInfo? Error { get; set; }
    public ResponseMeta Meta { get; set; } = new();

    public static ToolResponse<T> Ok(T data) => new()
    {
        Success = true,
        Data = data,
        Meta = new ResponseMeta { Timestamp = DateTime.UtcNow }
    };

    public static ToolResponse<T> Fail(string code, string message, string? details = null) => new()
    {
        Success = false,
        Error = new ErrorInfo { Code = code, Message = message, Details = details },
        Meta = new ResponseMeta { Timestamp = DateTime.UtcNow }
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions.Default);
}

public class ErrorInfo
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
}

public class ResponseMeta
{
    public long DurationMs { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public static class ErrorCodes
{
    public const string MemoryNotFound = "MEMORY_NOT_FOUND";
    public const string CategoryNotFound = "CATEGORY_NOT_FOUND";
    public const string TagNotFound = "TAG_NOT_FOUND";
    public const string RevisionNotFound = "REVISION_NOT_FOUND";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string DuplicateDetected = "DUPLICATE_DETECTED";
    public const string CategoryNotEmpty = "CATEGORY_NOT_EMPTY";
    public const string CategoryDepthExceeded = "CATEGORY_DEPTH_EXCEEDED";
    public const string ContentTooLarge = "CONTENT_TOO_LARGE";
    public const string InvalidLinkType = "INVALID_LINK_TYPE";
    public const string SelfLink = "SELF_LINK";
    public const string BackupFailed = "BACKUP_FAILED";
    public const string RestoreFailed = "RESTORE_FAILED";
    public const string DbCorrupted = "DB_CORRUPTED";
    public const string EmbeddingFailed = "EMBEDDING_FAILED";
    public const string ModelNotFound = "MODEL_NOT_FOUND";
    public const string ImportFailed = "IMPORT_FAILED";
    public const string StorageFull = "STORAGE_FULL";
    public const string DatabaseBusy = "DATABASE_BUSY";
    public const string UnknownError = "UNKNOWN_ERROR";
}

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}
