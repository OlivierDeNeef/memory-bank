namespace MemoryBank.Core.Models;

public class Tag
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public int UsageCount { get; set; }
}
