namespace DeepMind.Core.Models;

public class Memory
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Content { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? CategoryId { get; set; }
    public MemoryType Type { get; set; } = MemoryType.Fact;
    public int Priority { get; set; } = 3;
    public bool IsPinned { get; set; }
    public bool IsArchived { get; set; }
    public int AccessCount { get; set; }
    public int RevisionNumber { get; set; } = 1;
    public int? TokenCount { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string? Source { get; set; }
    public string? Metadata { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAccessed { get; set; }

    // Navigation (not stored, populated by queries)
    public List<string> Tags { get; set; } = [];
    public string? CategoryPath { get; set; }
}

public enum MemoryType
{
    Fact,
    Decision,
    Procedure,
    Reference,
    Observation
}
