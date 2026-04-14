using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using DeepMind.Core.Configuration;
using DeepMind.Core.Models;

namespace DeepMind.Core.Storage;

public class MemoryStore
{
    private readonly DeepMindDb _db;
    private readonly ILogger<MemoryStore> _logger;
    private readonly DeepMindConfiguration _config;

    public MemoryStore(DeepMindDb db, DeepMindConfiguration config, ILogger<MemoryStore> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    // ── Memory CRUD ──────────────────────────────────────────────────

    public Memory Insert(Memory memory, List<Chunk> chunks)
    {
        using var tx = _db.BeginTransaction();
        try
        {
            InsertMemory(memory, tx);

            foreach (var tag in memory.Tags)
                EnsureTagLink(memory.Id, tag, tx);

            foreach (var chunk in chunks)
            {
                chunk.MemoryId = memory.Id;
                InsertChunk(chunk, tx);
            }

            WriteAudit(memory.Id, "created", null, tx);
            tx.Commit();

            _logger.LogInformation("Memory {MemoryId} stored with {ChunkCount} chunks", memory.Id, chunks.Count);
            return memory;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public Memory? GetById(string id)
    {
        using var cmd = _db.CreateCommand("""
            SELECT m.*, c.name as category_name
            FROM memories m
            LEFT JOIN categories c ON c.id = m.category_id
            WHERE m.id = @id
            """);
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var memory = ReadMemory(reader);
        memory.Tags = GetTagsForMemory(id);
        memory.CategoryPath = memory.CategoryId != null ? GetCategoryPath(memory.CategoryId) : null;
        return memory;
    }

    public Memory? Update(string id, string? content, string? summary, int? priority,
        List<string>? tags, string? categoryPath, string? type, string? metadata, string? reason)
    {
        var existing = GetById(id);
        if (existing == null) return null;

        using var tx = _db.BeginTransaction();
        try
        {
            // Create revision snapshot of current state
            InsertRevision(new Revision
            {
                MemoryId = id,
                RevisionNumber = existing.RevisionNumber,
                Content = existing.Content,
                Summary = existing.Summary,
                Reason = reason ?? "updated",
                CreatedAt = DateTime.UtcNow
            }, tx);

            var setClauses = new List<string> { "updated_at = @now", "revision_number = revision_number + 1" };
            using var cmd = _db.CreateCommand("", tx);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));

            if (content != null) { setClauses.Add("content = @content"); cmd.Parameters.AddWithValue("@content", content); }
            if (summary != null) { setClauses.Add("summary = @summary"); cmd.Parameters.AddWithValue("@summary", summary); }
            if (priority.HasValue) { setClauses.Add("priority = @priority"); cmd.Parameters.AddWithValue("@priority", priority.Value); }
            if (type != null) { setClauses.Add("type = @type"); cmd.Parameters.AddWithValue("@type", type); }
            if (metadata != null) { setClauses.Add("metadata = @metadata"); cmd.Parameters.AddWithValue("@metadata", metadata); }

            if (categoryPath != null)
            {
                var catId = EnsureCategoryPath(categoryPath, tx);
                setClauses.Add("category_id = @catId");
                cmd.Parameters.AddWithValue("@catId", catId);
            }

            cmd.CommandText = $"UPDATE memories SET {string.Join(", ", setClauses)} WHERE id = @id";
            cmd.ExecuteNonQuery();

            if (tags != null)
            {
                // Replace all tags
                using var delCmd = _db.CreateCommand("DELETE FROM memory_tags WHERE memory_id = @id", tx);
                delCmd.Parameters.AddWithValue("@id", id);
                delCmd.ExecuteNonQuery();

                foreach (var tag in tags)
                    EnsureTagLink(id, tag, tx);
            }

            WriteAudit(id, "updated", reason, tx);
            tx.Commit();

            return GetById(id);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public bool Delete(string id)
    {
        using var tx = _db.BeginTransaction();
        try
        {
            // Cascade handles chunks, embeddings, memory_tags, revisions
            using var cmd = _db.CreateCommand("DELETE FROM memories WHERE id = @id", tx);
            cmd.Parameters.AddWithValue("@id", id);
            var affected = cmd.ExecuteNonQuery();

            if (affected > 0)
            {
                // Clean up orphan tags left behind after cascade removed memory_tags rows
                using var cleanTags = _db.CreateCommand(
                    "DELETE FROM tags WHERE id NOT IN (SELECT DISTINCT tag_id FROM memory_tags)", tx);
                cleanTags.ExecuteNonQuery();

                // Clean up orphan categories (repeat until stable for nested hierarchies)
                CleanOrphanCategories(tx);

                WriteAudit(id, "deleted", null, tx);
            }

            tx.Commit();
            return affected > 0;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public int BulkDelete(string? categoryPath, string? tag, DateTime? dateBefore)
    {
        var conditions = new List<string>();
        using var cmd = _db.CreateCommand("");

        if (categoryPath != null)
        {
            var catId = FindCategoryByPath(categoryPath);
            if (catId != null)
            {
                conditions.Add("category_id = @catId");
                cmd.Parameters.AddWithValue("@catId", catId);
            }
            else return 0;
        }

        if (tag != null)
        {
            conditions.Add("id IN (SELECT mt.memory_id FROM memory_tags mt JOIN tags t ON t.id = mt.tag_id WHERE t.name = @tag)");
            cmd.Parameters.AddWithValue("@tag", tag.ToLowerInvariant());
        }

        if (dateBefore.HasValue)
        {
            conditions.Add("created_at < @dateBefore");
            cmd.Parameters.AddWithValue("@dateBefore", dateBefore.Value.ToString("o"));
        }

        if (conditions.Count == 0) return 0;

        cmd.CommandText = $"DELETE FROM memories WHERE {string.Join(" AND ", conditions)}";
        var affected = cmd.ExecuteNonQuery();

        if (affected > 0)
        {
            // Clean up orphan tags left behind after cascade removed memory_tags rows
            using var cleanTags = _db.CreateCommand(
                "DELETE FROM tags WHERE id NOT IN (SELECT DISTINCT tag_id FROM memory_tags)");
            cleanTags.ExecuteNonQuery();

            // Clean up orphan categories (repeat until stable for nested hierarchies)
            CleanOrphanCategories();
        }

        return affected;
    }

    public void SetPinned(string id, bool pinned)
    {
        using var cmd = _db.CreateCommand("UPDATE memories SET is_pinned = @pinned, updated_at = @now WHERE id = @id");
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@pinned", pinned ? 1 : 0);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public List<Memory> GetPinnedMemories()
    {
        using var cmd = _db.CreateCommand("SELECT * FROM memories WHERE is_pinned = 1 AND is_archived = 0");
        var memories = new List<Memory>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var memory = ReadMemory(reader);
            memory.Tags = GetTagsForMemory(memory.Id);
            memory.CategoryPath = memory.CategoryId != null ? GetCategoryPath(memory.CategoryId) : null;
            memories.Add(memory);
        }
        return memories;
    }

    public void SetArchived(string id, bool archived)
    {
        using var cmd = _db.CreateCommand("UPDATE memories SET is_archived = @archived, updated_at = @now WHERE id = @id");
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@archived", archived ? 1 : 0);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public void IncrementAccessCount(string id)
    {
        using var cmd = _db.CreateCommand(
            "UPDATE memories SET access_count = access_count + 1, last_accessed = @now WHERE id = @id");
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public bool Exists(string id)
    {
        using var cmd = _db.CreateCommand("SELECT 1 FROM memories WHERE id = @id");
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteScalar() != null;
    }

    // ── Chunks ───────────────────────────────────────────────────────

    public List<Chunk> GetChunks(string memoryId, int? fromIndex = null, int? toIndex = null)
    {
        var sql = "SELECT * FROM chunks WHERE memory_id = @mid";
        if (fromIndex.HasValue) sql += " AND chunk_index >= @from";
        if (toIndex.HasValue) sql += " AND chunk_index <= @to";
        sql += " ORDER BY chunk_index";

        using var cmd = _db.CreateCommand(sql);
        cmd.Parameters.AddWithValue("@mid", memoryId);
        if (fromIndex.HasValue) cmd.Parameters.AddWithValue("@from", fromIndex.Value);
        if (toIndex.HasValue) cmd.Parameters.AddWithValue("@to", toIndex.Value);

        var chunks = new List<Chunk>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            chunks.Add(new Chunk
            {
                Id = reader.GetString(reader.GetOrdinal("id")),
                MemoryId = reader.GetString(reader.GetOrdinal("memory_id")),
                ChunkIndex = reader.GetInt32(reader.GetOrdinal("chunk_index")),
                Content = reader.GetString(reader.GetOrdinal("content")),
                Summary = reader.IsDBNull(reader.GetOrdinal("summary")) ? null : reader.GetString(reader.GetOrdinal("summary")),
                Keywords = reader.IsDBNull(reader.GetOrdinal("keywords")) ? null : reader.GetString(reader.GetOrdinal("keywords")),
                TokenCount = reader.IsDBNull(reader.GetOrdinal("token_count")) ? null : reader.GetInt32(reader.GetOrdinal("token_count")),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")))
            });
        }
        return chunks;
    }

    public int GetChunkCount(string memoryId)
    {
        using var cmd = _db.CreateCommand("SELECT COUNT(*) FROM chunks WHERE memory_id = @mid");
        cmd.Parameters.AddWithValue("@mid", memoryId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void InsertChunks(List<Chunk> chunks)
    {
        using var tx = _db.BeginTransaction();
        try
        {
            foreach (var chunk in chunks)
                InsertChunk(chunk, tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void DeleteChunksForMemory(string memoryId, SqliteTransaction? tx = null)
    {
        using var cmd = _db.CreateCommand("DELETE FROM chunks WHERE memory_id = @mid", tx);
        cmd.Parameters.AddWithValue("@mid", memoryId);
        cmd.ExecuteNonQuery();
    }

    // ── Revisions ────────────────────────────────────────────────────

    public List<Revision> GetRevisions(string memoryId)
    {
        using var cmd = _db.CreateCommand(
            "SELECT * FROM revisions WHERE memory_id = @mid ORDER BY revision_number");
        cmd.Parameters.AddWithValue("@mid", memoryId);

        var revisions = new List<Revision>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            revisions.Add(new Revision
            {
                Id = reader.GetString(reader.GetOrdinal("id")),
                MemoryId = reader.GetString(reader.GetOrdinal("memory_id")),
                RevisionNumber = reader.GetInt32(reader.GetOrdinal("revision_number")),
                Content = reader.GetString(reader.GetOrdinal("content")),
                Summary = reader.IsDBNull(reader.GetOrdinal("summary")) ? null : reader.GetString(reader.GetOrdinal("summary")),
                Reason = reader.IsDBNull(reader.GetOrdinal("reason")) ? null : reader.GetString(reader.GetOrdinal("reason")),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")))
            });
        }
        return revisions;
    }

    public Revision? GetRevision(string memoryId, int revisionNumber)
    {
        using var cmd = _db.CreateCommand(
            "SELECT * FROM revisions WHERE memory_id = @mid AND revision_number = @rn");
        cmd.Parameters.AddWithValue("@mid", memoryId);
        cmd.Parameters.AddWithValue("@rn", revisionNumber);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new Revision
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            MemoryId = reader.GetString(reader.GetOrdinal("memory_id")),
            RevisionNumber = reader.GetInt32(reader.GetOrdinal("revision_number")),
            Content = reader.GetString(reader.GetOrdinal("content")),
            Summary = reader.IsDBNull(reader.GetOrdinal("summary")) ? null : reader.GetString(reader.GetOrdinal("summary")),
            Reason = reader.IsDBNull(reader.GetOrdinal("reason")) ? null : reader.GetString(reader.GetOrdinal("reason")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")))
        };
    }

    // ── Categories ───────────────────────────────────────────────────

    public string EnsureCategoryPath(string path, SqliteTransaction? tx = null)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? parentId = null;

        foreach (var segment in segments)
        {
            var existing = FindCategory(segment, parentId, tx);
            if (existing != null)
            {
                parentId = existing;
            }
            else
            {
                var id = Guid.NewGuid().ToString();
                using var cmd = _db.CreateCommand(
                    "INSERT INTO categories (id, name, parent_id) VALUES (@id, @name, @pid)", tx);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@name", segment);
                cmd.Parameters.AddWithValue("@pid", (object?)parentId ?? DBNull.Value);
                cmd.ExecuteNonQuery();
                parentId = id;
            }
        }

        return parentId!;
    }

    public List<Category> GetCategories(string? parentId = null)
    {
        var sql = parentId == null
            ? "SELECT c.*, (SELECT COUNT(*) FROM memories m WHERE m.category_id = c.id) as memory_count FROM categories c WHERE c.parent_id IS NULL"
            : "SELECT c.*, (SELECT COUNT(*) FROM memories m WHERE m.category_id = c.id) as memory_count FROM categories c WHERE c.parent_id = @pid";

        using var cmd = _db.CreateCommand(sql);
        if (parentId != null) cmd.Parameters.AddWithValue("@pid", parentId);

        var categories = new List<Category>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var cat = new Category
            {
                Id = reader.GetString(reader.GetOrdinal("id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                ParentId = reader.IsDBNull(reader.GetOrdinal("parent_id")) ? null : reader.GetString(reader.GetOrdinal("parent_id")),
                Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
                MemoryCount = reader.GetInt32(reader.GetOrdinal("memory_count"))
            };
            cat.Path = GetCategoryPath(cat.Id);
            categories.Add(cat);
        }
        return categories;
    }

    public List<Category> GetAllCategories()
    {
        using var cmd = _db.CreateCommand(
            "SELECT c.*, (SELECT COUNT(*) FROM memories m WHERE m.category_id = c.id) as memory_count FROM categories c ORDER BY c.name");

        var categories = new List<Category>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var cat = new Category
            {
                Id = reader.GetString(reader.GetOrdinal("id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                ParentId = reader.IsDBNull(reader.GetOrdinal("parent_id")) ? null : reader.GetString(reader.GetOrdinal("parent_id")),
                Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
                MemoryCount = reader.GetInt32(reader.GetOrdinal("memory_count"))
            };
            cat.Path = GetCategoryPath(cat.Id);
            categories.Add(cat);
        }
        return categories;
    }

    public bool DeleteCategory(string id)
    {
        // Check if empty
        using var countCmd = _db.CreateCommand("SELECT COUNT(*) FROM memories WHERE category_id = @id");
        countCmd.Parameters.AddWithValue("@id", id);
        if (Convert.ToInt32(countCmd.ExecuteScalar()) > 0) return false;

        // Check no children
        using var childCmd = _db.CreateCommand("SELECT COUNT(*) FROM categories WHERE parent_id = @id");
        childCmd.Parameters.AddWithValue("@id", id);
        if (Convert.ToInt32(childCmd.ExecuteScalar()) > 0) return false;

        using var cmd = _db.CreateCommand("DELETE FROM categories WHERE id = @id");
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public void RenameCategory(string id, string newName)
    {
        using var cmd = _db.CreateCommand("UPDATE categories SET name = @name WHERE id = @id");
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@name", newName);
        cmd.ExecuteNonQuery();
    }

    public string GetCategoryPath(string categoryId)
    {
        var parts = new List<string>();
        var currentId = categoryId;

        while (currentId != null)
        {
            using var cmd = _db.CreateCommand("SELECT name, parent_id FROM categories WHERE id = @id");
            cmd.Parameters.AddWithValue("@id", currentId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) break;
            parts.Insert(0, reader.GetString(0));
            currentId = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        return string.Join("/", parts);
    }

    private void CleanOrphanCategories(SqliteTransaction? tx = null)
    {
        int batch;
        do
        {
            using var cmd = _db.CreateCommand("""
                DELETE FROM categories WHERE id NOT IN (
                    SELECT DISTINCT category_id FROM memories WHERE category_id IS NOT NULL
                ) AND id NOT IN (
                    SELECT DISTINCT parent_id FROM categories WHERE parent_id IS NOT NULL
                )
                """, tx);
            batch = cmd.ExecuteNonQuery();
        } while (batch > 0);
    }

    public string? FindCategoryByPath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? parentId = null;

        foreach (var segment in segments)
        {
            var found = FindCategory(segment, parentId);
            if (found == null) return null;
            parentId = found;
        }

        return parentId;
    }

    // Get category ID and all descendant category IDs
    public List<string> GetCategoryAndDescendantIds(string categoryId)
    {
        var ids = new List<string> { categoryId };
        var queue = new Queue<string>();
        queue.Enqueue(categoryId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            using var cmd = _db.CreateCommand("SELECT id FROM categories WHERE parent_id = @pid");
            cmd.Parameters.AddWithValue("@pid", current);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var childId = reader.GetString(0);
                ids.Add(childId);
                queue.Enqueue(childId);
            }
        }

        return ids;
    }

    // ── Tags ─────────────────────────────────────────────────────────

    public List<Tag> GetTags(string sort = "name", int? limit = null)
    {
        var sql = """
            SELECT t.id, t.name, COUNT(mt.memory_id) as usage_count
            FROM tags t
            LEFT JOIN memory_tags mt ON mt.tag_id = t.id
            GROUP BY t.id, t.name
            """;

        sql += sort == "most_used" ? " ORDER BY usage_count DESC" : " ORDER BY t.name";
        if (limit.HasValue) sql += $" LIMIT {limit.Value}";

        using var cmd = _db.CreateCommand(sql);
        var tags = new List<Tag>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            tags.Add(new Tag
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                UsageCount = reader.GetInt32(2)
            });
        }
        return tags;
    }

    public void RenameTag(string oldName, string newName)
    {
        using var cmd = _db.CreateCommand("UPDATE tags SET name = @new WHERE name = @old");
        cmd.Parameters.AddWithValue("@old", oldName.ToLowerInvariant());
        cmd.Parameters.AddWithValue("@new", newName.ToLowerInvariant());
        cmd.ExecuteNonQuery();
    }

    public void MergeTags(string sourceTag, string targetTag)
    {
        using var tx = _db.BeginTransaction();
        try
        {
            var sourceId = FindTagId(sourceTag, tx);
            var targetId = FindTagId(targetTag, tx);
            if (sourceId == null || targetId == null) return;

            // Move all memory associations from source to target (ignore duplicates)
            using var moveCmd = _db.CreateCommand("""
                INSERT OR IGNORE INTO memory_tags (memory_id, tag_id)
                SELECT memory_id, @targetId FROM memory_tags WHERE tag_id = @sourceId
                """, tx);
            moveCmd.Parameters.AddWithValue("@sourceId", sourceId);
            moveCmd.Parameters.AddWithValue("@targetId", targetId);
            moveCmd.ExecuteNonQuery();

            // Delete source tag associations and tag itself
            using var delMt = _db.CreateCommand("DELETE FROM memory_tags WHERE tag_id = @sourceId", tx);
            delMt.Parameters.AddWithValue("@sourceId", sourceId);
            delMt.ExecuteNonQuery();

            using var delTag = _db.CreateCommand("DELETE FROM tags WHERE id = @sourceId", tx);
            delTag.Parameters.AddWithValue("@sourceId", sourceId);
            delTag.ExecuteNonQuery();

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ── Links ────────────────────────────────────────────────────────

    public void LinkMemories(string sourceId, string targetId, string linkType)
    {
        using var cmd = _db.CreateCommand(
            "INSERT OR REPLACE INTO memory_links (source_id, target_id, link_type) VALUES (@s, @t, @lt)");
        cmd.Parameters.AddWithValue("@s", sourceId);
        cmd.Parameters.AddWithValue("@t", targetId);
        cmd.Parameters.AddWithValue("@lt", linkType);
        cmd.ExecuteNonQuery();
    }

    public void UnlinkMemories(string sourceId, string targetId)
    {
        using var cmd = _db.CreateCommand(
            "DELETE FROM memory_links WHERE source_id = @s AND target_id = @t");
        cmd.Parameters.AddWithValue("@s", sourceId);
        cmd.Parameters.AddWithValue("@t", targetId);
        cmd.ExecuteNonQuery();
    }

    public List<Memory> GetLinkedMemories(string id, string? linkType = null)
    {
        var sql = """
            SELECT m.* FROM memories m
            JOIN memory_links ml ON ml.target_id = m.id
            WHERE ml.source_id = @id
            """;
        if (linkType != null) sql += " AND ml.link_type = @lt";

        using var cmd = _db.CreateCommand(sql);
        cmd.Parameters.AddWithValue("@id", id);
        if (linkType != null) cmd.Parameters.AddWithValue("@lt", linkType);

        var memories = new List<Memory>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            memories.Add(ReadMemory(reader));
        }
        return memories;
    }

    // ── Embeddings ───────────────────────────────────────────────────

    public void InsertEmbedding(string chunkId, string memoryId, float[] embedding, string model, SqliteTransaction? tx = null)
    {
        using var cmd = _db.CreateCommand("""
            INSERT INTO embeddings (id, chunk_id, memory_id, embedding, model, created_at)
            VALUES (@id, @cid, @mid, @emb, @model, @now)
            """, tx);
        cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("@cid", chunkId);
        cmd.Parameters.AddWithValue("@mid", memoryId);
        cmd.Parameters.AddWithValue("@emb", FloatsToBlob(embedding));
        cmd.Parameters.AddWithValue("@model", model);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public List<(string ChunkId, string MemoryId, float[] Embedding)> GetAllEmbeddings()
    {
        using var cmd = _db.CreateCommand("SELECT chunk_id, memory_id, embedding FROM embeddings");
        var results = new List<(string, string, float[])>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add((
                reader.GetString(0),
                reader.GetString(1),
                BlobToFloats((byte[])reader.GetValue(2))
            ));
        }
        return results;
    }

    public void DeleteEmbeddingsForMemory(string memoryId, SqliteTransaction? tx = null)
    {
        using var cmd = _db.CreateCommand("DELETE FROM embeddings WHERE memory_id = @mid", tx);
        cmd.Parameters.AddWithValue("@mid", memoryId);
        cmd.ExecuteNonQuery();
    }

    public void DeleteEmbeddingsForChunk(string chunkId, SqliteTransaction? tx = null)
    {
        using var cmd = _db.CreateCommand("DELETE FROM embeddings WHERE chunk_id = @cid", tx);
        cmd.Parameters.AddWithValue("@cid", chunkId);
        cmd.ExecuteNonQuery();
    }

    // ── Stats ────────────────────────────────────────────────────────

    public Dictionary<string, object> GetStats()
    {
        var stats = new Dictionary<string, object>();

        using (var cmd = _db.CreateCommand("SELECT COUNT(*) FROM memories")) stats["totalMemories"] = Convert.ToInt32(cmd.ExecuteScalar());
        using (var cmd = _db.CreateCommand("SELECT COUNT(*) FROM chunks")) stats["totalChunks"] = Convert.ToInt32(cmd.ExecuteScalar());
        using (var cmd = _db.CreateCommand("SELECT COUNT(*) FROM revisions")) stats["totalRevisions"] = Convert.ToInt32(cmd.ExecuteScalar());
        using (var cmd = _db.CreateCommand("SELECT COUNT(*) FROM categories")) stats["totalCategories"] = Convert.ToInt32(cmd.ExecuteScalar());
        using (var cmd = _db.CreateCommand("SELECT COUNT(*) FROM tags")) stats["totalTags"] = Convert.ToInt32(cmd.ExecuteScalar());
        using (var cmd = _db.CreateCommand("SELECT COUNT(*) FROM embeddings")) stats["totalEmbeddings"] = Convert.ToInt32(cmd.ExecuteScalar());

        return stats;
    }

    // ── Audit ────────────────────────────────────────────────────────

    public void WriteAudit(string? memoryId, string action, string? details, SqliteTransaction? tx = null)
    {
        using var cmd = _db.CreateCommand("""
            INSERT INTO audit_log (id, memory_id, action, details, created_at)
            VALUES (@id, @mid, @action, @details, @now)
            """, tx);
        cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("@mid", (object?)memoryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@action", action);
        cmd.Parameters.AddWithValue("@details", (object?)details ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    // ── Private helpers ──────────────────────────────────────────────

    private void InsertMemory(Memory memory, SqliteTransaction tx)
    {
        using var cmd = _db.CreateCommand("""
            INSERT INTO memories (id, content, summary, category_id, type, priority, is_pinned, is_archived,
                access_count, revision_number, token_count, valid_from, valid_until, source, metadata, created_at, updated_at)
            VALUES (@id, @content, @summary, @catId, @type, @priority, @pinned, @archived,
                @access, @rev, @tokens, @vfrom, @vuntil, @source, @meta, @created, @updated)
            """, tx);

        cmd.Parameters.AddWithValue("@id", memory.Id);
        cmd.Parameters.AddWithValue("@content", memory.Content);
        cmd.Parameters.AddWithValue("@summary", (object?)memory.Summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@catId", (object?)memory.CategoryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@type", memory.Type.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@priority", memory.Priority);
        cmd.Parameters.AddWithValue("@pinned", memory.IsPinned ? 1 : 0);
        cmd.Parameters.AddWithValue("@archived", memory.IsArchived ? 1 : 0);
        cmd.Parameters.AddWithValue("@access", memory.AccessCount);
        cmd.Parameters.AddWithValue("@rev", memory.RevisionNumber);
        cmd.Parameters.AddWithValue("@tokens", (object?)memory.TokenCount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@vfrom", (object?)memory.ValidFrom?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@vuntil", (object?)memory.ValidUntil?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@source", (object?)memory.Source ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@meta", (object?)memory.Metadata ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created", memory.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@updated", memory.UpdatedAt.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    private void InsertChunk(Chunk chunk, SqliteTransaction tx)
    {
        using var cmd = _db.CreateCommand("""
            INSERT INTO chunks (id, memory_id, chunk_index, content, summary, keywords, token_count, created_at)
            VALUES (@id, @mid, @idx, @content, @summary, @keywords, @tokens, @now)
            """, tx);
        cmd.Parameters.AddWithValue("@id", chunk.Id);
        cmd.Parameters.AddWithValue("@mid", chunk.MemoryId);
        cmd.Parameters.AddWithValue("@idx", chunk.ChunkIndex);
        cmd.Parameters.AddWithValue("@content", chunk.Content);
        cmd.Parameters.AddWithValue("@summary", (object?)chunk.Summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@keywords", (object?)chunk.Keywords ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tokens", (object?)chunk.TokenCount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", chunk.CreatedAt.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Update a chunk's summary and keywords (triggers FTS5 re-index via UPDATE trigger).
    /// </summary>
    public void UpdateChunkEnrichment(string chunkId, string? summary, string? keywords)
    {
        var setClauses = new List<string>();
        using var cmd = _db.CreateCommand("");
        cmd.Parameters.AddWithValue("@id", chunkId);

        if (summary != null) { setClauses.Add("summary = @summary"); cmd.Parameters.AddWithValue("@summary", summary); }
        if (keywords != null) { setClauses.Add("keywords = @keywords"); cmd.Parameters.AddWithValue("@keywords", keywords); }

        if (setClauses.Count == 0) return;

        cmd.CommandText = $"UPDATE chunks SET {string.Join(", ", setClauses)} WHERE id = @id";
        cmd.ExecuteNonQuery();
    }

    private void InsertRevision(Revision revision, SqliteTransaction tx)
    {
        using var cmd = _db.CreateCommand("""
            INSERT INTO revisions (id, memory_id, revision_number, content, summary, reason, created_at)
            VALUES (@id, @mid, @rn, @content, @summary, @reason, @now)
            """, tx);
        cmd.Parameters.AddWithValue("@id", revision.Id);
        cmd.Parameters.AddWithValue("@mid", revision.MemoryId);
        cmd.Parameters.AddWithValue("@rn", revision.RevisionNumber);
        cmd.Parameters.AddWithValue("@content", revision.Content);
        cmd.Parameters.AddWithValue("@summary", (object?)revision.Summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reason", (object?)revision.Reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", revision.CreatedAt.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    private void EnsureTagLink(string memoryId, string tagName, SqliteTransaction tx)
    {
        tagName = tagName.ToLowerInvariant().Trim();
        if (string.IsNullOrEmpty(tagName)) return;

        // Ensure tag exists
        using var upsert = _db.CreateCommand(
            "INSERT OR IGNORE INTO tags (id, name) VALUES (@id, @name)", tx);
        upsert.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        upsert.Parameters.AddWithValue("@name", tagName);
        upsert.ExecuteNonQuery();

        // Get tag id
        using var getId = _db.CreateCommand("SELECT id FROM tags WHERE name = @name", tx);
        getId.Parameters.AddWithValue("@name", tagName);
        var tagId = getId.ExecuteScalar()!.ToString()!;

        // Link
        using var link = _db.CreateCommand(
            "INSERT OR IGNORE INTO memory_tags (memory_id, tag_id) VALUES (@mid, @tid)", tx);
        link.Parameters.AddWithValue("@mid", memoryId);
        link.Parameters.AddWithValue("@tid", tagId);
        link.ExecuteNonQuery();
    }

    public List<string> GetTagsForMemoryPublic(string memoryId) => GetTagsForMemory(memoryId);

    private List<string> GetTagsForMemory(string memoryId)
    {
        using var cmd = _db.CreateCommand("""
            SELECT t.name FROM tags t
            JOIN memory_tags mt ON mt.tag_id = t.id
            WHERE mt.memory_id = @mid
            ORDER BY t.name
            """);
        cmd.Parameters.AddWithValue("@mid", memoryId);

        var tags = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) tags.Add(reader.GetString(0));
        return tags;
    }

    private string? FindCategory(string name, string? parentId, SqliteTransaction? tx = null)
    {
        var sql = parentId == null
            ? "SELECT id FROM categories WHERE name = @name AND parent_id IS NULL"
            : "SELECT id FROM categories WHERE name = @name AND parent_id = @pid";

        using var cmd = _db.CreateCommand(sql, tx);
        cmd.Parameters.AddWithValue("@name", name);
        if (parentId != null) cmd.Parameters.AddWithValue("@pid", parentId);
        return cmd.ExecuteScalar()?.ToString();
    }

    private string? FindTagId(string tagName, SqliteTransaction? tx = null)
    {
        using var cmd = _db.CreateCommand("SELECT id FROM tags WHERE name = @name", tx);
        cmd.Parameters.AddWithValue("@name", tagName.ToLowerInvariant());
        return cmd.ExecuteScalar()?.ToString();
    }

    private static Memory ReadMemory(SqliteDataReader reader)
    {
        return new Memory
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            Content = reader.GetString(reader.GetOrdinal("content")),
            Summary = reader.IsDBNull(reader.GetOrdinal("summary")) ? null : reader.GetString(reader.GetOrdinal("summary")),
            CategoryId = reader.IsDBNull(reader.GetOrdinal("category_id")) ? null : reader.GetString(reader.GetOrdinal("category_id")),
            Type = Enum.Parse<MemoryType>(reader.GetString(reader.GetOrdinal("type")), ignoreCase: true),
            Priority = reader.GetInt32(reader.GetOrdinal("priority")),
            IsPinned = reader.GetInt32(reader.GetOrdinal("is_pinned")) == 1,
            IsArchived = reader.GetInt32(reader.GetOrdinal("is_archived")) == 1,
            AccessCount = reader.GetInt32(reader.GetOrdinal("access_count")),
            RevisionNumber = reader.GetInt32(reader.GetOrdinal("revision_number")),
            TokenCount = reader.IsDBNull(reader.GetOrdinal("token_count")) ? null : reader.GetInt32(reader.GetOrdinal("token_count")),
            Source = reader.IsDBNull(reader.GetOrdinal("source")) ? null : reader.GetString(reader.GetOrdinal("source")),
            Metadata = reader.IsDBNull(reader.GetOrdinal("metadata")) ? null : reader.GetString(reader.GetOrdinal("metadata")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("updated_at"))),
            LastAccessed = reader.IsDBNull(reader.GetOrdinal("last_accessed")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("last_accessed")))
        };
    }

    private static byte[] FloatsToBlob(float[] floats)
    {
        var bytes = new byte[floats.Length * sizeof(float)];
        Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BlobToFloats(byte[] bytes)
    {
        var floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }
}
