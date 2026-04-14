using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using DeepMind.Core.Models;
using DeepMind.Core.Storage;

namespace DeepMind.Server.Resources;

[McpServerResourceType]
public class IndexResource
{
    private readonly MemoryStore _store;

    public IndexResource(MemoryStore store)
    {
        _store = store;
    }

    [McpServerResource(UriTemplate = "deepmind://index", Name = "DeepMind Knowledge Index",
        MimeType = "application/json")]
    [Description("Current categories, top tags, memory types, and stats. Loaded once per session for AI orientation.")]
    public string GetIndex()
    {
        var categories = _store.GetAllCategories();
        var tags = _store.GetTags("most_used", 30);
        var stats = _store.GetStats();

        var index = new
        {
            categories = categories.Select(c => new
            {
                path = c.Path,
                count = c.MemoryCount
            }).Where(c => c.count > 0),
            topTags = tags.Select(t => new
            {
                name = t.Name,
                count = t.UsageCount
            }),
            memoryTypes = new[] { "fact", "decision", "procedure", "reference", "observation" },
            totalMemories = stats["totalMemories"],
            totalCategories = stats["totalCategories"],
            totalTags = stats["totalTags"]
        };

        return JsonSerializer.Serialize(index, JsonOptions.Default);
    }
}
