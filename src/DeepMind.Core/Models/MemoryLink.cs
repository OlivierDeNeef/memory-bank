namespace DeepMind.Core.Models;

public class MemoryLink
{
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public LinkType LinkType { get; set; }
}

public enum LinkType
{
    Related,
    Supersedes,
    Contradicts,
    Extends
}
