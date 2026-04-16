namespace MemoryBank.Core.Models;

public class Category
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public string? Description { get; set; }

    // Navigation
    public int MemoryCount { get; set; }
    public string Path { get; set; } = string.Empty;
}
