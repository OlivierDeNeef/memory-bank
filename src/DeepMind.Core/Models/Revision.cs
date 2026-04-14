namespace DeepMind.Core.Models;

public class Revision
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string MemoryId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
