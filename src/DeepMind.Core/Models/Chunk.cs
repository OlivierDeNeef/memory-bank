namespace DeepMind.Core.Models;

/// <summary>
/// Context from the parent memory, passed to chunking for summary enrichment.
/// </summary>
public class MemoryContext
{
    public string? Summary { get; set; }
    public string? CategoryPath { get; set; }
    public List<string>? Tags { get; set; }
}

public class Chunk
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string MemoryId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Keywords { get; set; }
    public int? TokenCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
