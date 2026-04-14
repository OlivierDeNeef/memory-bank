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
public class ExportImportTool
{
    private readonly MemoryStore _store;
    private readonly ChunkingService _chunking;
    private readonly OnnxEmbeddingService _embeddings;
    private readonly DeepMindDb _db;
    private readonly DeepMindConfiguration _config;

    public ExportImportTool(MemoryStore store, ChunkingService chunking,
        OnnxEmbeddingService embeddings, DeepMindDb db, DeepMindConfiguration config)
    {
        _store = store;
        _chunking = chunking;
        _embeddings = embeddings;
        _db = db;
        _config = config;
    }

    [McpServerTool(Name = "export"), Description("Export memories to JSON. Optionally filter by category or tag.")]
    public string Export(
        [Description("Filter by category path")] string? category = null,
        [Description("Filter by tag")] string? tag = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var conditions = new List<string>();
        using var cmd = _db.CreateCommand("");

        if (category != null)
        {
            var catId = _store.FindCategoryByPath(category);
            if (catId != null)
            {
                var catIds = _store.GetCategoryAndDescendantIds(catId);
                conditions.Add($"m.category_id IN ({string.Join(",", catIds.Select((_, i) => $"@cat{i}"))})");
                for (int i = 0; i < catIds.Count; i++)
                    cmd.Parameters.AddWithValue($"@cat{i}", catIds[i]);
            }
        }

        if (tag != null)
        {
            conditions.Add("m.id IN (SELECT mt.memory_id FROM memory_tags mt JOIN tags t ON t.id = mt.tag_id WHERE t.name = @tag)");
            cmd.Parameters.AddWithValue("@tag", tag.ToLowerInvariant());
        }

        var where = conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : "";
        cmd.CommandText = $"SELECT m.* FROM memories m {where} ORDER BY m.created_at";

        var memories = new List<object>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(reader.GetOrdinal("id"));
            var tags = _store.GetTagsForMemoryPublic(id);
            var categoryId = reader.IsDBNull(reader.GetOrdinal("category_id")) ? null : reader.GetString(reader.GetOrdinal("category_id"));

            memories.Add(new
            {
                id,
                content = reader.GetString(reader.GetOrdinal("content")),
                summary = reader.IsDBNull(reader.GetOrdinal("summary")) ? null : reader.GetString(reader.GetOrdinal("summary")),
                category = categoryId != null ? _store.GetCategoryPath(categoryId) : null,
                type = reader.GetString(reader.GetOrdinal("type")),
                priority = reader.GetInt32(reader.GetOrdinal("priority")),
                isPinned = reader.GetInt32(reader.GetOrdinal("is_pinned")) == 1,
                isArchived = reader.GetInt32(reader.GetOrdinal("is_archived")) == 1,
                tags,
                source = reader.IsDBNull(reader.GetOrdinal("source")) ? null : reader.GetString(reader.GetOrdinal("source")),
                metadata = reader.IsDBNull(reader.GetOrdinal("metadata")) ? null : reader.GetString(reader.GetOrdinal("metadata")),
                createdAt = reader.GetString(reader.GetOrdinal("created_at")),
                updatedAt = reader.GetString(reader.GetOrdinal("updated_at"))
            });
        }

        sw.Stop();

        var response = ToolResponse<object>.Ok(new
        {
            memoriesCount = memories.Count,
            memories
        });
        response.Meta.DurationMs = sw.ElapsedMilliseconds;

        return response.ToJson();
    }

    [McpServerTool(Name = "import"), Description("Import memories from JSON data. Supports conflict resolution: skip, overwrite.")]
    public string Import(
        [Description("JSON array of memory objects with content, category, tags, type, priority fields")] string data,
        [Description("Conflict resolution: 'skip' or 'overwrite'")] string conflictResolution = "skip")
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        List<JsonElement>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<JsonElement>>(data, JsonOptions.Default);
        }
        catch (Exception ex)
        {
            return ToolResponse<object>.Fail(ErrorCodes.ImportFailed, $"Invalid JSON: {ex.Message}").ToJson();
        }

        if (items == null || items.Count == 0)
            return ToolResponse<object>.Fail(ErrorCodes.ImportFailed, "No memories found in import data").ToJson();

        int imported = 0, skipped = 0, failed = 0;

        foreach (var item in items)
        {
            try
            {
                var content = item.GetProperty("content").GetString()!;
                var categoryPath = item.TryGetProperty("category", out var catEl) ? catEl.GetString() : null;
                var type = item.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "fact" : "fact";
                var priority = item.TryGetProperty("priority", out var priEl) ? priEl.GetInt32() : 3;
                var tags = item.TryGetProperty("tags", out var tagsEl)
                    ? tagsEl.EnumerateArray().Select(t => t.GetString()!).ToList()
                    : new List<string>();
                var summary = item.TryGetProperty("summary", out var sumEl) ? sumEl.GetString() : null;
                var source = item.TryGetProperty("source", out var srcEl) ? srcEl.GetString() : "import";
                var metadata = item.TryGetProperty("metadata", out var metaEl) ? metaEl.GetRawText() : null;

                // Check for existing id (for overwrite)
                var existingId = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (existingId != null && _store.Exists(existingId))
                {
                    if (conflictResolution == "skip") { skipped++; continue; }
                    // Overwrite
                    _store.Delete(existingId);
                }

                var memory = new Memory
                {
                    Id = existingId ?? Guid.NewGuid().ToString(),
                    Content = content,
                    Summary = summary ?? (content.Length > 500 ? content[..500] : content),
                    Type = Enum.Parse<MemoryType>(type, ignoreCase: true),
                    Priority = priority,
                    Source = source,
                    Metadata = metadata,
                    Tags = tags,
                    TokenCount = _chunking.EstimateTokenCount(content)
                };

                if (categoryPath != null)
                    memory.CategoryId = _store.EnsureCategoryPath(categoryPath);

                var chunks = _chunking.ChunkText(content, memory.Id);
                _store.Insert(memory, chunks);

                foreach (var chunk in chunks)
                {
                    var embedding = _embeddings.GenerateEmbedding(chunk.Content);
                    if (embedding != null)
                        _store.InsertEmbedding(chunk.Id, memory.Id, embedding, _config.Embedding.ModelName);
                }

                imported++;
            }
            catch
            {
                failed++;
            }
        }

        sw.Stop();

        var response = ToolResponse<object>.Ok(new
        {
            imported,
            skipped,
            failed,
            total = items.Count
        });
        response.Meta.DurationMs = sw.ElapsedMilliseconds;

        return response.ToJson();
    }

    [McpServerTool(Name = "reembed_all"), Description("Regenerate all embeddings with the current model. Use after switching embedding models.")]
    public string ReembedAll()
    {
        if (!_embeddings.IsAvailable)
            return ToolResponse<object>.Fail(ErrorCodes.ModelNotFound,
                "No embedding model available. Place an ONNX model at the configured path.").ToJson();

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Get all chunks
        using var cmd = _db.CreateCommand("SELECT id, memory_id, content FROM chunks");
        var chunks = new List<(string Id, string MemoryId, string Content)>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
                chunks.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        // Delete all existing embeddings
        using var delCmd = _db.CreateCommand("DELETE FROM embeddings");
        delCmd.ExecuteNonQuery();

        int processed = 0, failed = 0;
        foreach (var (chunkId, memoryId, content) in chunks)
        {
            var embedding = _embeddings.GenerateEmbedding(content);
            if (embedding != null)
            {
                _store.InsertEmbedding(chunkId, memoryId, embedding, _config.Embedding.ModelName);
                processed++;
            }
            else
            {
                failed++;
            }
        }

        sw.Stop();

        var response = ToolResponse<object>.Ok(new
        {
            totalChunks = chunks.Count,
            processed,
            failed,
            model = _config.Embedding.ModelName
        });
        response.Meta.DurationMs = sw.ElapsedMilliseconds;

        return response.ToJson();
    }

    [McpServerTool(Name = "cleanup_orphans"), Description("Delete tags and categories that have no associated memories.")]
    public string CleanupOrphans()
    {
        using var orphanTags = _db.CreateCommand("""
            DELETE FROM tags WHERE id NOT IN (SELECT DISTINCT tag_id FROM memory_tags)
            """);
        var deletedTags = orphanTags.ExecuteNonQuery();

        // Delete leaf categories with no memories and no children (repeat until stable)
        var deletedCategories = 0;
        int batch;
        do
        {
            using var orphanCats = _db.CreateCommand("""
                DELETE FROM categories WHERE id NOT IN (
                    SELECT DISTINCT category_id FROM memories WHERE category_id IS NOT NULL
                ) AND id NOT IN (
                    SELECT DISTINCT parent_id FROM categories WHERE parent_id IS NOT NULL
                )
                """);
            batch = orphanCats.ExecuteNonQuery();
            deletedCategories += batch;
        } while (batch > 0);

        return ToolResponse<object>.Ok(new
        {
            deletedTags,
            deletedCategories
        }).ToJson();
    }
}
