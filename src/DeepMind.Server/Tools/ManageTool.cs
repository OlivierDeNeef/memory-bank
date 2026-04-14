using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using DeepMind.Core.Configuration;
using DeepMind.Core.Embeddings;
using DeepMind.Core.Models;
using DeepMind.Core.Search;
using DeepMind.Core.Storage;

namespace DeepMind.Server.Tools;

[McpServerToolType]
public class ManageTool
{
    private readonly MemoryStore _store;
    private readonly ChunkingService _chunking;
    private readonly OnnxEmbeddingService _embeddings;
    private readonly DeepMindConfiguration _config;

    public ManageTool(MemoryStore store, ChunkingService chunking,
        OnnxEmbeddingService embeddings, DeepMindConfiguration config)
    {
        _store = store;
        _chunking = chunking;
        _embeddings = embeddings;
        _config = config;
    }

    [McpServerTool(Name = "get_memory"), Description("Get the full content and metadata of a specific memory by ID.")]
    public string GetMemory([Description("Memory UUID")] string id)
    {
        var memory = _store.GetById(id);
        if (memory == null)
            return ToolResponse<object>.Fail(ErrorCodes.MemoryNotFound, $"No memory found with id '{id}'").ToJson();

        _store.IncrementAccessCount(id);

        return ToolResponse<object>.Ok(new
        {
            id = memory.Id,
            content = memory.Content,
            summary = memory.Summary,
            category = memory.CategoryPath,
            type = memory.Type.ToString().ToLowerInvariant(),
            priority = memory.Priority,
            tags = memory.Tags,
            isPinned = memory.IsPinned,
            isArchived = memory.IsArchived,
            accessCount = memory.AccessCount,
            revisionNumber = memory.RevisionNumber,
            tokenCount = memory.TokenCount,
            source = memory.Source,
            metadata = memory.Metadata,
            createdAt = memory.CreatedAt.ToString("o"),
            updatedAt = memory.UpdatedAt.ToString("o"),
            lastAccessed = memory.LastAccessed?.ToString("o"),
            chunkCount = _store.GetChunkCount(id)
        }).ToJson();
    }

    [McpServerTool(Name = "get_chunks"), Description("Get chunks of a large memory. Use after recall returns a chunked result.")]
    public string GetChunks(
        [Description("Memory UUID")] string memoryId,
        [Description("Start chunk index")] int? fromIndex = null,
        [Description("End chunk index")] int? toIndex = null)
    {
        if (!_store.Exists(memoryId))
            return ToolResponse<object>.Fail(ErrorCodes.MemoryNotFound, $"No memory found with id '{memoryId}'").ToJson();

        var chunks = _store.GetChunks(memoryId, fromIndex, toIndex);

        return ToolResponse<object>.Ok(new
        {
            memoryId,
            chunks = chunks.Select(c => new
            {
                chunkIndex = c.ChunkIndex,
                content = c.Content,
                summary = c.Summary,
                tokenCount = c.TokenCount
            }),
            totalChunks = _store.GetChunkCount(memoryId)
        }).ToJson();
    }

    [McpServerTool(Name = "update_memory"), Description("Update a memory's content, metadata, or organization. Creates a revision snapshot of the previous state.")]
    public string UpdateMemory(
        [Description("Memory UUID")] string id,
        [Description("New content")] string? content = null,
        [Description("New summary")] string? summary = null,
        [Description("New priority (1-5)")] int? priority = null,
        [Description("Replace tags (comma-separated)")] string? tags = null,
        [Description("Move to category path")] string? category = null,
        [Description("Change type")] string? type = null,
        [Description("JSON metadata (merged with existing)")] string? metadata = null,
        [Description("Reason for this update")] string? reason = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (priority.HasValue && (priority < 1 || priority > 5))
            return ToolResponse<object>.Fail(ErrorCodes.ValidationFailed, "Priority must be between 1 and 5").ToJson();

        var tagList = tags != null
            ? tags.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim().ToLowerInvariant()).ToList()
            : null;

        var updated = _store.Update(id, content, summary, priority, tagList, category, type, metadata, reason);
        if (updated == null)
            return ToolResponse<object>.Fail(ErrorCodes.MemoryNotFound, $"No memory found with id '{id}'").ToJson();

        // Re-chunk and re-embed if content changed
        var chunksRegenerated = false;
        if (content != null)
        {
            _store.DeleteEmbeddingsForMemory(id);
            _store.DeleteChunksForMemory(id);

            var chunks = _chunking.ChunkText(content, id);
            _store.InsertChunks(chunks);

            foreach (var chunk in chunks)
            {
                var embedding = _embeddings.GenerateEmbedding(chunk.Content);
                if (embedding != null)
                    _store.InsertEmbedding(chunk.Id, id, embedding, _config.Embedding.ModelName);
            }
            chunksRegenerated = true;
        }

        sw.Stop();

        var response = ToolResponse<object>.Ok(new
        {
            id = updated.Id,
            revisionNumber = updated.RevisionNumber,
            previousRevision = updated.RevisionNumber - 1,
            reason,
            chunksRegenerated
        });
        response.Meta.DurationMs = sw.ElapsedMilliseconds;

        return response.ToJson();
    }

    [McpServerTool(Name = "forget"), Description("Delete a specific memory and all its chunks, revisions, embeddings, and links.")]
    public string Forget([Description("Memory UUID")] string id)
    {
        if (!_store.Exists(id))
            return ToolResponse<object>.Fail(ErrorCodes.MemoryNotFound, $"No memory found with id '{id}'").ToJson();

        var chunkCount = _store.GetChunkCount(id);
        var revisions = _store.GetRevisions(id);
        var deleted = _store.Delete(id);

        return ToolResponse<object>.Ok(new
        {
            id,
            deleted,
            deletedChunks = chunkCount,
            deletedRevisions = revisions.Count
        }).ToJson();
    }

    [McpServerTool(Name = "bulk_forget"), Description("Delete multiple memories matching filters.")]
    public string BulkForget(
        [Description("Category path")] string? category = null,
        [Description("Tag name")] string? tag = null,
        [Description("Delete memories created before this date (ISO 8601)")] string? dateBefore = null)
    {
        var date = dateBefore != null ? DateTime.Parse(dateBefore) : (DateTime?)null;
        var count = _store.BulkDelete(category, tag, date);
        return ToolResponse<object>.Ok(new { deletedCount = count }).ToJson();
    }

    [McpServerTool(Name = "pin"), Description("Pin a memory so it always surfaces in relevant searches.")]
    public string Pin([Description("Memory UUID")] string id)
    {
        if (!_store.Exists(id))
            return ToolResponse<object>.Fail(ErrorCodes.MemoryNotFound, $"No memory found with id '{id}'").ToJson();

        _store.SetPinned(id, true);
        return ToolResponse<object>.Ok(new { id, isPinned = true }).ToJson();
    }

    [McpServerTool(Name = "unpin"), Description("Unpin a memory.")]
    public string Unpin([Description("Memory UUID")] string id)
    {
        if (!_store.Exists(id))
            return ToolResponse<object>.Fail(ErrorCodes.MemoryNotFound, $"No memory found with id '{id}'").ToJson();

        _store.SetPinned(id, false);
        return ToolResponse<object>.Ok(new { id, isPinned = false }).ToJson();
    }

    [McpServerTool(Name = "archive"), Description("Archive a memory (hide from search, keep for history).")]
    public string Archive([Description("Memory UUID")] string id)
    {
        if (!_store.Exists(id))
            return ToolResponse<object>.Fail(ErrorCodes.MemoryNotFound, $"No memory found with id '{id}'").ToJson();

        _store.SetArchived(id, true);
        return ToolResponse<object>.Ok(new { id, isArchived = true }).ToJson();
    }

    [McpServerTool(Name = "unarchive"), Description("Unarchive a memory.")]
    public string Unarchive([Description("Memory UUID")] string id)
    {
        if (!_store.Exists(id))
            return ToolResponse<object>.Fail(ErrorCodes.MemoryNotFound, $"No memory found with id '{id}'").ToJson();

        _store.SetArchived(id, false);
        return ToolResponse<object>.Ok(new { id, isArchived = false }).ToJson();
    }

    [McpServerTool(Name = "link_memories"), Description("Create a typed relationship between two memories.")]
    public string LinkMemories(
        [Description("Source memory UUID")] string sourceId,
        [Description("Target memory UUID")] string targetId,
        [Description("Link type: related, supersedes, contradicts, extends")] string linkType)
    {
        if (sourceId == targetId)
            return ToolResponse<object>.Fail(ErrorCodes.SelfLink, "Cannot link a memory to itself").ToJson();
        if (!_store.Exists(sourceId))
            return ToolResponse<object>.Fail(ErrorCodes.MemoryNotFound, $"Source memory '{sourceId}' not found").ToJson();
        if (!_store.Exists(targetId))
            return ToolResponse<object>.Fail(ErrorCodes.MemoryNotFound, $"Target memory '{targetId}' not found").ToJson();

        var validTypes = new[] { "related", "supersedes", "contradicts", "extends" };
        if (!validTypes.Contains(linkType.ToLowerInvariant()))
            return ToolResponse<object>.Fail(ErrorCodes.InvalidLinkType,
                $"Invalid link type '{linkType}'. Must be one of: {string.Join(", ", validTypes)}").ToJson();

        _store.LinkMemories(sourceId, targetId, linkType.ToLowerInvariant());
        return ToolResponse<object>.Ok(new { sourceId, targetId, linkType }).ToJson();
    }

    [McpServerTool(Name = "unlink_memories"), Description("Remove a relationship between two memories.")]
    public string UnlinkMemories(
        [Description("Source memory UUID")] string sourceId,
        [Description("Target memory UUID")] string targetId)
    {
        _store.UnlinkMemories(sourceId, targetId);
        return ToolResponse<object>.Ok(new { sourceId, targetId, unlinked = true }).ToJson();
    }

    [McpServerTool(Name = "get_linked"), Description("Get memories linked to a specific memory.")]
    public string GetLinked(
        [Description("Memory UUID")] string id,
        [Description("Filter by link type")] string? linkType = null)
    {
        if (!_store.Exists(id))
            return ToolResponse<object>.Fail(ErrorCodes.MemoryNotFound, $"No memory found with id '{id}'").ToJson();

        var linked = _store.GetLinkedMemories(id, linkType);
        return ToolResponse<object>.Ok(new
        {
            memoryId = id,
            linked = linked.Select(m => new
            {
                id = m.Id,
                summary = m.Summary,
                type = m.Type.ToString().ToLowerInvariant(),
                priority = m.Priority
            })
        }).ToJson();
    }

    [McpServerTool(Name = "memory_stats"), Description("Get statistics about the memory store.")]
    public string MemoryStats()
    {
        var stats = _store.GetStats();
        return ToolResponse<object>.Ok(stats).ToJson();
    }
}
