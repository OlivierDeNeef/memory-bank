using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using MemoryBank.Core.Configuration;
using MemoryBank.Core.Embeddings;
using MemoryBank.Core.Models;
using MemoryBank.Core.Search;
using MemoryBank.Core.Storage;

namespace MemoryBank.Server.Tools;

[McpServerToolType]
public class RememberTool
{
    private readonly MemoryStore _store;
    private readonly ChunkingService _chunking;
    private readonly OnnxEmbeddingService _embeddings;
    private readonly HybridSearchEngine _search;
    private readonly MemoryBankConfiguration _config;

    public RememberTool(MemoryStore store, ChunkingService chunking,
        OnnxEmbeddingService embeddings, HybridSearchEngine search, MemoryBankConfiguration config)
    {
        _store = store;
        _chunking = chunking;
        _embeddings = embeddings;
        _search = search;
        _config = config;
    }

    [McpServerTool(Name = "remember"), Description("IMPORTANT: Do NOT call this tool directly. Use the memorybank:remember skill instead, which handles classification, duplicate detection, chunk enrichment, and subagent delegation. --- Store a new memory with content, category, tags, priority, and type. Returns the memory ID and chunk count.")]
    public string Remember(
        [Description("The content to remember (1-100000 chars)")] string content,
        [Description("Category path, e.g. 'projects/backend' (auto-created)")] string? category = null,
        [Description("Priority 1-5: trivial(1), low(2), normal(3), high(4), critical(5)")] int priority = 3,
        [Description("Tags as comma-separated values, e.g. 'auth,jwt,security'")] string? tags = null,
        [Description("Type: fact, decision, procedure, reference, observation")] string type = "fact",
        [Description("Source identifier, e.g. 'conversation', 'manual'")] string? source = null,
        [Description("Short summary (auto-generated if omitted)")] string? summary = null,
        [Description("JSON metadata object")] string? metadata = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Validation
        if (string.IsNullOrWhiteSpace(content))
            return ToolResponse<object>.Fail(ErrorCodes.ValidationFailed, "Content cannot be empty").ToJson();

        if (content.Length > _config.Validation.MaxContentLength)
            return ToolResponse<object>.Fail(ErrorCodes.ContentTooLarge,
                $"Content exceeds maximum length of {_config.Validation.MaxContentLength}").ToJson();

        if (priority < 1 || priority > 5)
            return ToolResponse<object>.Fail(ErrorCodes.ValidationFailed, "Priority must be between 1 and 5").ToJson();

        if (!Enum.TryParse<MemoryType>(type, ignoreCase: true, out var memoryType))
            return ToolResponse<object>.Fail(ErrorCodes.ValidationFailed,
                $"Invalid type '{type}'. Must be one of: fact, decision, procedure, reference, observation").ToJson();

        var tagList = ParseTags(tags);
        if (tagList.Count > _config.Validation.MaxTagsPerMemory)
            return ToolResponse<object>.Fail(ErrorCodes.ValidationFailed,
                $"Too many tags. Maximum is {_config.Validation.MaxTagsPerMemory}").ToJson();

        // Create memory
        var memory = new Memory
        {
            Content = content,
            Summary = summary ?? (content.Length > 500 ? content[..500] : content),
            Type = memoryType,
            Priority = priority,
            Source = source,
            Metadata = metadata,
            Tags = tagList,
            TokenCount = _chunking.EstimateTokenCount(content)
        };

        // Set category
        if (category != null)
        {
            memory.CategoryId = _store.EnsureCategoryPath(category);
            memory.CategoryPath = category;
        }

        // Build context for chunk enrichment
        var memoryContext = new MemoryContext
        {
            Summary = memory.Summary,
            CategoryPath = category,
            Tags = tagList.Count > 0 ? tagList : null
        };

        // Chunk content with memory context for rich summaries
        var chunks = _chunking.ChunkText(content, memory.Id, memoryContext);

        // Duplicate detection
        object? duplicateWarning = null;
        var similar = _search.FindSimilar(content, _config.Memory.DuplicateThreshold, 1);
        if (similar.Count > 0)
        {
            duplicateWarning = new
            {
                existingId = similar[0].Id,
                existingSummary = similar[0].Summary,
                similarity = MathF.Round(similar[0].Similarity, 2)
            };
        }

        // Store
        _store.Insert(memory, chunks);

        // Generate embeddings for each chunk (prepend context for better vector search)
        var embeddingPrefix = BuildEmbeddingPrefix(memory, category, tagList);
        foreach (var chunk in chunks)
        {
            var textToEmbed = embeddingPrefix + chunk.Content;
            var embedding = _embeddings.GenerateEmbedding(textToEmbed);
            if (embedding != null)
                _store.InsertEmbedding(chunk.Id, memory.Id, embedding, _config.Embedding.ModelName);
        }

        sw.Stop();

        // Return chunk previews so the LLM can enrich them
        var chunkPreviews = chunks.Count > 1
            ? chunks.Select(c => new
            {
                chunkIndex = c.ChunkIndex,
                preview = c.Content.Length > 200 ? c.Content[..200] + "..." : c.Content
            }).ToList<object>()
            : null;

        var response = ToolResponse<object>.Ok(new
        {
            id = memory.Id,
            summary = memory.Summary,
            chunkCount = chunks.Count,
            duplicateWarning,
            chunkPreviews,
            enrichHint = chunks.Count > 1
                ? "This memory was split into chunks. Call enrich_chunks to add per-chunk keywords and summaries for better search."
                : null
        });
        response.Meta.DurationMs = sw.ElapsedMilliseconds;

        return response.ToJson();
    }

    private static string BuildEmbeddingPrefix(Memory memory, string? category, List<string> tags)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(memory.Summary))
            parts.Add(memory.Summary);
        if (!string.IsNullOrWhiteSpace(category))
            parts.Add(category);
        if (tags.Count > 0)
            parts.Add(string.Join(", ", tags));

        return parts.Count > 0 ? string.Join(". ", parts) + ". " : "";
    }

    private static List<string> ParseTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags)) return [];
        return tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();
    }
}
