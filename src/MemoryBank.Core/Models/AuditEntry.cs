namespace MemoryBank.Core.Models;

public class AuditEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? MemoryId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
