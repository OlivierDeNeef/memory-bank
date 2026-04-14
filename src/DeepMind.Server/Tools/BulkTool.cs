using System.ComponentModel;
using ModelContextProtocol.Server;
using DeepMind.Core.Models;
using DeepMind.Core.Storage;

namespace DeepMind.Server.Tools;

[McpServerToolType]
public class BulkTool
{
    private readonly MemoryStore _store;

    public BulkTool(MemoryStore store)
    {
        _store = store;
    }

    [McpServerTool(Name = "bulk_tag"), Description("Add a tag to multiple memories at once.")]
    public string BulkTag(
        [Description("Comma-separated memory UUIDs")] string memoryIds,
        [Description("Tag to add")] string tag)
    {
        var ids = memoryIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).ToList();
        int updated = 0, notFound = 0;

        foreach (var id in ids)
        {
            if (!_store.Exists(id)) { notFound++; continue; }

            var memory = _store.GetById(id)!;
            var currentTags = memory.Tags.ToList();
            if (!currentTags.Contains(tag.ToLowerInvariant()))
            {
                currentTags.Add(tag.ToLowerInvariant());
                _store.Update(id, null, null, null, currentTags, null, null, null, $"bulk: added tag '{tag}'");
                updated++;
            }
        }

        return ToolResponse<object>.Ok(new { updated, notFound, total = ids.Count }).ToJson();
    }

    [McpServerTool(Name = "bulk_recategorize"), Description("Move multiple memories to a new category.")]
    public string BulkRecategorize(
        [Description("Comma-separated memory UUIDs")] string memoryIds,
        [Description("Target category path")] string categoryPath)
    {
        var ids = memoryIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).ToList();
        int updated = 0, notFound = 0;

        foreach (var id in ids)
        {
            if (!_store.Exists(id)) { notFound++; continue; }
            _store.Update(id, null, null, null, null, categoryPath, null, null, $"bulk: moved to {categoryPath}");
            updated++;
        }

        return ToolResponse<object>.Ok(new { updated, notFound, total = ids.Count, category = categoryPath }).ToJson();
    }

    [McpServerTool(Name = "bulk_priority"), Description("Update priority for multiple memories.")]
    public string BulkPriority(
        [Description("Comma-separated memory UUIDs")] string memoryIds,
        [Description("New priority (1-5)")] int priority)
    {
        if (priority < 1 || priority > 5)
            return ToolResponse<object>.Fail(ErrorCodes.ValidationFailed, "Priority must be between 1 and 5").ToJson();

        var ids = memoryIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).ToList();
        int updated = 0, notFound = 0;

        foreach (var id in ids)
        {
            if (!_store.Exists(id)) { notFound++; continue; }
            _store.Update(id, null, null, priority, null, null, null, null, $"bulk: priority set to {priority}");
            updated++;
        }

        return ToolResponse<object>.Ok(new { updated, notFound, total = ids.Count, priority }).ToJson();
    }
}
