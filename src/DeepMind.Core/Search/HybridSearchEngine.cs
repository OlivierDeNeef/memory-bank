using Microsoft.Extensions.Logging;
using DeepMind.Core.Configuration;
using DeepMind.Core.Embeddings;
using DeepMind.Core.Models;
using DeepMind.Core.Storage;

namespace DeepMind.Core.Search;

public class HybridSearchEngine
{
    private readonly MemoryStore _store;
    private readonly OnnxEmbeddingService _embeddings;
    private readonly DeepMindDb _db;
    private readonly SearchConfig _searchConfig;
    private readonly ILogger<HybridSearchEngine> _logger;

    public HybridSearchEngine(
        MemoryStore store,
        OnnxEmbeddingService embeddings,
        DeepMindDb db,
        SearchConfig searchConfig,
        ILogger<HybridSearchEngine> logger)
    {
        _store = store;
        _embeddings = embeddings;
        _db = db;
        _searchConfig = searchConfig;
        _logger = logger;
    }

    public SearchResponse Search(SearchRequest request)
    {
        var keywordResults = KeywordSearch(request);
        var vectorResults = _embeddings.IsAvailable ? VectorSearch(request) : new Dictionary<string, (float Score, string ChunkContent, int ChunkIndex, int TotalChunks)>();

        // Merge results
        var allMemoryIds = keywordResults.Keys.Union(vectorResults.Keys).ToHashSet();
        var scored = new List<SearchResult>();

        foreach (var memoryId in allMemoryIds)
        {
            var result = ScoreAndFilter(memoryId, request, keywordResults, vectorResults);
            if (result != null)
                scored.Add(result);
        }

        return FinalizeResults(scored, request);
    }

    /// <summary>
    /// Search recent memories by date without requiring a text query.
    /// </summary>
    public SearchResponse SearchRecent(SearchRequest request)
    {
        // Direct DB query for recent memories
        var memories = GetRecentMemories(request);
        var scored = new List<SearchResult>();

        foreach (var memory in memories)
        {
            var chunkCount = _store.GetChunkCount(memory.Id);
            scored.Add(new SearchResult
            {
                Memory = memory,
                VectorScore = 0,
                KeywordScore = 0,
                PriorityScore = memory.Priority / 5f,
                FinalScore = memory.Priority / 5f,
                IsChunked = chunkCount > 1,
                Freshness = GetFreshness(memory)
            });
        }

        return FinalizeResults(scored, request);
    }

    public bool ExistsSearch(string query, string? category)
    {
        var request = new SearchRequest { Query = query, Category = category, Limit = 1 };
        return Search(request).TotalCount > 0;
    }

    public int CountSearch(string? query, string? category, List<string>? tags, MemoryType? type)
    {
        if (string.IsNullOrWhiteSpace(query))
            return CountDirect(category, tags, type);

        var request = new SearchRequest
        {
            Query = query,
            Category = category,
            Tags = tags,
            Type = type,
            Limit = 10000
        };
        return Search(request).TotalCount;
    }

    /// <summary>
    /// Find memories similar to the given content. Used for duplicate detection.
    /// </summary>
    public List<(string Id, string Summary, float Similarity)> FindSimilar(string content, float threshold, int limit = 5)
    {
        if (!_embeddings.IsAvailable) return [];

        var queryEmbedding = _embeddings.GenerateEmbedding(content);
        if (queryEmbedding == null) return [];

        var allEmbeddings = _store.GetAllEmbeddings();
        var seen = new Dictionary<string, float>();

        foreach (var (_, memoryId, embedding) in allEmbeddings)
        {
            var similarity = OnnxEmbeddingService.CosineSimilarity(queryEmbedding, embedding);
            if (similarity >= threshold)
            {
                if (!seen.ContainsKey(memoryId) || seen[memoryId] < similarity)
                    seen[memoryId] = similarity;
            }
        }

        return seen
            .OrderByDescending(kv => kv.Value)
            .Take(limit)
            .Select(kv =>
            {
                var memory = _store.GetById(kv.Key);
                return (kv.Key, memory?.Summary ?? "", kv.Value);
            })
            .ToList();
    }

    // ── Private: scoring and filtering ───────────────────────────────

    private SearchResult? ScoreAndFilter(
        string memoryId,
        SearchRequest request,
        Dictionary<string, float> keywordResults,
        Dictionary<string, (float Score, string ChunkContent, int ChunkIndex, int TotalChunks)> vectorResults)
    {
        var memory = _store.GetById(memoryId);
        if (memory == null) return null;

        // Apply filters
        if (!request.IncludeArchived && memory.IsArchived) return null;
        if (request.MinPriority.HasValue && memory.Priority < request.MinPriority.Value) return null;
        if (request.Type.HasValue && memory.Type != request.Type.Value) return null;
        if (request.DateFrom.HasValue && memory.CreatedAt < request.DateFrom.Value) return null;
        if (request.DateTo.HasValue && memory.CreatedAt > request.DateTo.Value) return null;

        // Validity period filter — exclude expired memories by default
        if (memory.ValidUntil.HasValue && memory.ValidUntil.Value < DateTime.UtcNow && !request.IncludeArchived)
            return null;

        if (request.Category != null)
        {
            var catId = _store.FindCategoryByPath(request.Category);
            if (catId != null)
            {
                var validCatIds = _store.GetCategoryAndDescendantIds(catId);
                if (memory.CategoryId == null || !validCatIds.Contains(memory.CategoryId))
                    return null;
            }
        }

        if (request.Tags != null && request.Tags.Count > 0)
        {
            var memTags = memory.Tags.Select(t => t.ToLowerInvariant()).ToHashSet();
            var searchTags = request.Tags.Select(t => t.ToLowerInvariant()).ToList();

            if (request.TagMode == "and")
            {
                if (!searchTags.All(t => memTags.Contains(t))) return null;
            }
            else
            {
                if (!searchTags.Any(t => memTags.Contains(t))) return null;
            }
        }

        var keywordScore = keywordResults.TryGetValue(memoryId, out var ks) ? ks : 0f;
        var vectorScore = vectorResults.TryGetValue(memoryId, out var vs) ? vs.Score : 0f;
        var priorityScore = memory.Priority / 5f;
        var pinBonus = memory.IsPinned ? _searchConfig.PinBonus : 0f;
        var recencyDays = (DateTime.UtcNow - memory.CreatedAt).TotalDays;
        var recencyDecay = (float)(recencyDays * _searchConfig.RecencyDecayPerDay / 100);
        var accessBoost = MathF.Log2(memory.AccessCount + 1) * _searchConfig.AccessBoostFactor / 100;

        var finalScore =
            (_searchConfig.VectorWeight * vectorScore) +
            (_searchConfig.KeywordWeight * keywordScore) +
            (_searchConfig.PriorityWeight * priorityScore) +
            pinBonus +
            accessBoost -
            recencyDecay;

        var chunkCount = _store.GetChunkCount(memoryId);
        var result = new SearchResult
        {
            Memory = memory,
            VectorScore = vectorScore,
            KeywordScore = keywordScore,
            PriorityScore = priorityScore,
            FinalScore = finalScore,
            IsChunked = chunkCount > 1,
            Freshness = GetFreshness(memory)
        };

        if (vectorResults.TryGetValue(memoryId, out var chunkInfo) && chunkCount > 1)
        {
            result.MatchedChunk = chunkInfo.ChunkContent;
            result.ChunkIndex = chunkInfo.ChunkIndex;
            result.TotalChunks = chunkInfo.TotalChunks;
        }

        _store.IncrementAccessCount(memoryId);
        return result;
    }

    private SearchResponse FinalizeResults(List<SearchResult> scored, SearchRequest request)
    {
        scored = request.Sort switch
        {
            "date" => scored.OrderByDescending(r => r.Memory.CreatedAt).ToList(),
            "priority" => scored.OrderByDescending(r => r.Memory.Priority).ToList(),
            "access_count" => scored.OrderByDescending(r => r.Memory.AccessCount).ToList(),
            "revision_count" => scored.OrderByDescending(r => r.Memory.RevisionNumber).ToList(),
            _ => scored.OrderByDescending(r => r.FinalScore).ToList()
        };

        var totalCount = scored.Count;
        var paged = scored.Skip(request.Offset).Take(request.Limit).ToList();

        _store.WriteAudit(null, "recalled", $"query={request.Query}, results={totalCount}");

        return new SearchResponse
        {
            Results = paged,
            TotalCount = totalCount,
            HasMore = request.Offset + request.Limit < totalCount
        };
    }

    // ── Keyword search via FTS5 ──────────────────────────────────────

    private Dictionary<string, float> KeywordSearch(SearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query)) return new();

        var ftsQuery = SanitizeFtsQuery(request.Query);
        if (string.IsNullOrWhiteSpace(ftsQuery)) return new();

        try
        {
            // Use bm25() with column weights: content=1.0, summary=2.0, keywords=3.0
            // Keywords column gets highest weight since it contains curated search terms
            using var cmd = _db.CreateCommand("""
                SELECT c.memory_id, bm25(chunks_fts, 1.0, 2.0, 3.0) as score
                FROM chunks_fts fts
                JOIN chunks c ON c.rowid = fts.rowid
                WHERE chunks_fts MATCH @query
                ORDER BY score
                LIMIT 200
                """);
            cmd.Parameters.AddWithValue("@query", ftsQuery);

            var results = new Dictionary<string, float>();
            var maxScore = 0f;

            // First pass: collect raw scores
            var rawResults = new List<(string MemoryId, float RawScore)>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var memoryId = reader.GetString(0);
                var rawScore = Math.Abs(reader.GetFloat(1));
                rawResults.Add((memoryId, rawScore));
                if (rawScore > maxScore) maxScore = rawScore;
            }

            // Normalize scores to 0..1 range using max score
            foreach (var (memoryId, rawScore) in rawResults)
            {
                var normalizedScore = maxScore > 0 ? rawScore / maxScore : 0f;
                if (!results.ContainsKey(memoryId) || results[memoryId] < normalizedScore)
                    results[memoryId] = normalizedScore;
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FTS5 search failed for query: {Query}", request.Query);
            return new();
        }
    }

    // ── Vector search ────────────────────────────────────────────────

    private Dictionary<string, (float Score, string ChunkContent, int ChunkIndex, int TotalChunks)> VectorSearch(SearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query)) return new();

        var queryEmbedding = _embeddings.GenerateEmbedding(request.Query);
        if (queryEmbedding == null) return new();

        var allEmbeddings = _store.GetAllEmbeddings();
        var results = new Dictionary<string, (float Score, string ChunkContent, int ChunkIndex, int TotalChunks)>();

        foreach (var (chunkId, memoryId, embedding) in allEmbeddings)
        {
            var similarity = OnnxEmbeddingService.CosineSimilarity(queryEmbedding, embedding);
            if (similarity < 0.1f) continue;

            if (!results.ContainsKey(memoryId) || results[memoryId].Score < similarity)
            {
                var chunks = _store.GetChunks(memoryId);
                var matchedChunk = chunks.FirstOrDefault(c => c.Id == chunkId);
                results[memoryId] = (
                    similarity,
                    matchedChunk?.Content ?? "",
                    matchedChunk?.ChunkIndex ?? 0,
                    chunks.Count
                );
            }
        }

        return results;
    }

    // ── Direct DB queries ────────────────────────────────────────────

    private List<Memory> GetRecentMemories(SearchRequest request)
    {
        var conditions = new List<string> { "m.is_archived = 0" };
        using var cmd = _db.CreateCommand("");

        if (request.DateFrom.HasValue)
        {
            conditions.Add("m.created_at >= @dateFrom");
            cmd.Parameters.AddWithValue("@dateFrom", request.DateFrom.Value.ToString("o"));
        }

        if (request.Category != null)
        {
            var catId = _store.FindCategoryByPath(request.Category);
            if (catId != null)
            {
                var catIds = _store.GetCategoryAndDescendantIds(catId);
                conditions.Add($"m.category_id IN ({string.Join(",", catIds.Select((_, i) => $"@cat{i}"))})");
                for (int i = 0; i < catIds.Count; i++)
                    cmd.Parameters.AddWithValue($"@cat{i}", catIds[i]);
            }
        }

        cmd.CommandText = $"""
            SELECT m.* FROM memories m
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY m.created_at DESC
            LIMIT {request.Limit}
            """;

        var memories = new List<Memory>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var memory = ReadMemoryFromReader(reader);
            memory.Tags = _store.GetTagsForMemoryPublic(memory.Id);
            memory.CategoryPath = memory.CategoryId != null ? _store.GetCategoryPath(memory.CategoryId) : null;
            memories.Add(memory);
        }
        return memories;
    }

    private int CountDirect(string? category, List<string>? tags, MemoryType? type)
    {
        var conditions = new List<string> { "is_archived = 0" };
        using var cmd = _db.CreateCommand("");

        if (category != null)
        {
            var catId = _store.FindCategoryByPath(category);
            if (catId != null)
            {
                var catIds = _store.GetCategoryAndDescendantIds(catId);
                conditions.Add($"category_id IN ({string.Join(",", catIds.Select((_, i) => $"@cat{i}"))})");
                for (int i = 0; i < catIds.Count; i++)
                    cmd.Parameters.AddWithValue($"@cat{i}", catIds[i]);
            }
        }

        if (type.HasValue)
        {
            conditions.Add("type = @type");
            cmd.Parameters.AddWithValue("@type", type.Value.ToString().ToLowerInvariant());
        }

        if (tags != null && tags.Count > 0)
        {
            conditions.Add($"id IN (SELECT mt.memory_id FROM memory_tags mt JOIN tags t ON t.id = mt.tag_id WHERE t.name IN ({string.Join(",", tags.Select((_, i) => $"@tag{i}"))}))");
            for (int i = 0; i < tags.Count; i++)
                cmd.Parameters.AddWithValue($"@tag{i}", tags[i].ToLowerInvariant());
        }

        cmd.CommandText = $"SELECT COUNT(*) FROM memories WHERE {string.Join(" AND ", conditions)}";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static string SanitizeFtsQuery(string query)
    {
        var parts = new List<string>();

        foreach (var word in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = word.Replace("\"", "").Replace("'", "");
            if (string.IsNullOrWhiteSpace(clean)) continue;

            // Support NOT operator for negation filters
            if (clean.Equals("NOT", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add("NOT");
                continue;
            }

            parts.Add(clean);
        }

        return string.Join(" ", parts);
    }

    private static string GetFreshness(Memory memory)
    {
        // If memory has a validity period, check that
        if (memory.ValidUntil.HasValue && memory.ValidUntil.Value < DateTime.UtcNow)
            return "expired";

        var days = (DateTime.UtcNow - memory.CreatedAt).TotalDays;
        return days switch
        {
            < 7 => "fresh",
            < 30 => "recent",
            < 90 => "aging",
            _ => "stale"
        };
    }

    private static Memory ReadMemoryFromReader(Microsoft.Data.Sqlite.SqliteDataReader reader)
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
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("updated_at"))),
            LastAccessed = reader.IsDBNull(reader.GetOrdinal("last_accessed")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("last_accessed")))
        };
    }
}
