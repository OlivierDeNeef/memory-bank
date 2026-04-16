using System.ComponentModel;
using ModelContextProtocol.Server;
using MemoryBank.Core.Models;
using MemoryBank.Core.Storage;

namespace MemoryBank.Server.Tools;

[McpServerToolType]
public class CategoryTool
{
    private readonly MemoryStore _store;

    public CategoryTool(MemoryStore store)
    {
        _store = store;
    }

    [McpServerTool(Name = "list_categories"), Description("List all categories with memory counts. Optionally filter by parent.")]
    public string ListCategories([Description("Parent category ID to list children of")] string? parentId = null)
    {
        var categories = parentId != null ? _store.GetCategories(parentId) : _store.GetAllCategories();

        return ToolResponse<object>.Ok(new
        {
            categories = categories.Select(c => new
            {
                id = c.Id,
                name = c.Name,
                path = c.Path,
                parentId = c.ParentId,
                description = c.Description,
                memoryCount = c.MemoryCount
            })
        }).ToJson();
    }

    [McpServerTool(Name = "create_category"), Description("Create a new category. Use slash-separated path to create nested categories.")]
    public string CreateCategory(
        [Description("Category path, e.g. 'projects/backend/auth'")] string path,
        [Description("Description of this category")] string? description = null)
    {
        var id = _store.EnsureCategoryPath(path);
        return ToolResponse<object>.Ok(new { id, path }).ToJson();
    }

    [McpServerTool(Name = "rename_category"), Description("Rename a category.")]
    public string RenameCategory(
        [Description("Category ID")] string id,
        [Description("New name")] string name)
    {
        _store.RenameCategory(id, name);
        return ToolResponse<object>.Ok(new { id, name }).ToJson();
    }

    [McpServerTool(Name = "delete_category"), Description("Delete an empty category. Fails if it contains memories or child categories.")]
    public string DeleteCategory([Description("Category ID")] string id)
    {
        var deleted = _store.DeleteCategory(id);
        if (!deleted)
            return ToolResponse<object>.Fail(ErrorCodes.CategoryNotEmpty,
                "Category is not empty or has child categories. Move or delete its contents first.").ToJson();

        return ToolResponse<object>.Ok(new { id, deleted = true }).ToJson();
    }

    [McpServerTool(Name = "move_memory"), Description("Move a memory to a different category.")]
    public string MoveMemory(
        [Description("Memory UUID")] string id,
        [Description("Target category path")] string categoryPath)
    {
        if (!_store.Exists(id))
            return ToolResponse<object>.Fail(ErrorCodes.MemoryNotFound, $"No memory found with id '{id}'").ToJson();

        _store.Update(id, null, null, null, null, categoryPath, null, null, "moved to " + categoryPath);
        return ToolResponse<object>.Ok(new { id, category = categoryPath }).ToJson();
    }

    [McpServerTool(Name = "list_tags"), Description("List all tags with usage counts.")]
    public string ListTags(
        [Description("Sort by: 'name' or 'most_used'")] string sort = "most_used",
        [Description("Max tags to return")] int? limit = null)
    {
        var tags = _store.GetTags(sort, limit);
        return ToolResponse<object>.Ok(new
        {
            tags = tags.Select(t => new { name = t.Name, usageCount = t.UsageCount })
        }).ToJson();
    }

    [McpServerTool(Name = "rename_tag"), Description("Rename a tag across all memories.")]
    public string RenameTag(
        [Description("Current tag name")] string oldName,
        [Description("New tag name")] string newName)
    {
        _store.RenameTag(oldName, newName);
        return ToolResponse<object>.Ok(new { oldName, newName }).ToJson();
    }

    [McpServerTool(Name = "merge_tags"), Description("Merge one tag into another, combining all memory associations.")]
    public string MergeTags(
        [Description("Tag to merge from (will be deleted)")] string sourceTag,
        [Description("Tag to merge into (will be kept)")] string targetTag)
    {
        _store.MergeTags(sourceTag, targetTag);
        return ToolResponse<object>.Ok(new { merged = sourceTag, into = targetTag }).ToJson();
    }
}
