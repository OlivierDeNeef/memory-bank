using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using DeepMind.Core.Configuration;
using DeepMind.Core.Embeddings;
using DeepMind.Core.Models;
using DeepMind.Core.Storage;

namespace DeepMind.Server.Tools;

[McpServerToolType]
public class EnrichChunksTool
{
    private readonly MemoryStore _store;
    private readonly OnnxEmbeddingService _embeddings;
    private readonly DeepMindConfiguration _config;

    public EnrichChunksTool(MemoryStore store, OnnxEmbeddingService embeddings, DeepMindConfiguration config)
    {
        _store = store;
        _embeddings = embeddings;
        _config = config;
    }

    [McpServerTool(Name = "enrich_chunks"), Description("Enrich chunks with LLM-generated summaries and keywords for better search. Call after remember when content was split into multiple chunks.")]
    public string EnrichChunks(
        [Description("Memory ID returned from remember")] string memoryId,
        [Description("JSON array of enrichments: [{\"chunkIndex\": 0, \"summary\": \"...\", \"keywords\": \"keyword1, keyword2, ...\"}]")] string enrichments)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(memoryId))
            return ToolResponse<object>.Fail(ErrorCodes.ValidationFailed, "memoryId is required").ToJson();

        var memory = _store.GetById(memoryId);
        if (memory == null)
            return ToolResponse<object>.Fail(ErrorCodes.MemoryNotFound, $"Memory {memoryId} not found").ToJson();

        List<ChunkEnrichment>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<ChunkEnrichment>>(enrichments,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            return ToolResponse<object>.Fail(ErrorCodes.ValidationFailed, $"Invalid enrichments JSON: {ex.Message}").ToJson();
        }

        if (items == null || items.Count == 0)
            return ToolResponse<object>.Fail(ErrorCodes.ValidationFailed, "Enrichments array is empty").ToJson();

        var chunks = _store.GetChunks(memoryId);
        var updated = 0;
        var reembedded = 0;

        // Build context prefix for re-embedding
        var embeddingPrefix = "";
        if (!string.IsNullOrWhiteSpace(memory.Summary))
            embeddingPrefix += memory.Summary + ". ";
        if (!string.IsNullOrWhiteSpace(memory.CategoryPath))
            embeddingPrefix += memory.CategoryPath + ". ";
        if (memory.Tags.Count > 0)
            embeddingPrefix += string.Join(", ", memory.Tags) + ". ";

        foreach (var item in items)
        {
            var chunk = chunks.FirstOrDefault(c => c.ChunkIndex == item.ChunkIndex);
            if (chunk == null) continue;

            // Build enriched summary: context prefix + LLM summary
            var enrichedSummary = item.Summary;
            if (!string.IsNullOrWhiteSpace(enrichedSummary))
            {
                var contextPrefix = $"[{memory.Summary} | {memory.CategoryPath}] ";
                enrichedSummary = contextPrefix + enrichedSummary;
            }

            // Merge keywords: existing tags + LLM-provided keywords
            var allKeywords = new List<string>();
            if (memory.Tags.Count > 0)
                allKeywords.AddRange(memory.Tags);
            if (!string.IsNullOrWhiteSpace(item.Keywords))
                allKeywords.AddRange(item.Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()));
            var mergedKeywords = string.Join(", ", allKeywords.Distinct(StringComparer.OrdinalIgnoreCase));

            // Update chunk in DB (triggers FTS5 re-index)
            _store.UpdateChunkEnrichment(chunk.Id, enrichedSummary, mergedKeywords);
            updated++;

            // Re-generate embedding with enriched context
            var textToEmbed = embeddingPrefix;
            if (!string.IsNullOrWhiteSpace(mergedKeywords))
                textToEmbed += mergedKeywords + ". ";
            textToEmbed += chunk.Content;

            var embedding = _embeddings.GenerateEmbedding(textToEmbed);
            if (embedding != null)
            {
                _store.DeleteEmbeddingsForChunk(chunk.Id);
                _store.InsertEmbedding(chunk.Id, memoryId, embedding, _config.Embedding.ModelName);
                reembedded++;
            }
        }

        sw.Stop();

        var response = ToolResponse<object>.Ok(new
        {
            memoryId,
            chunksUpdated = updated,
            chunksReembedded = reembedded,
            totalChunks = chunks.Count
        });
        response.Meta.DurationMs = sw.ElapsedMilliseconds;

        return response.ToJson();
    }

    private class ChunkEnrichment
    {
        public int ChunkIndex { get; set; }
        public string? Summary { get; set; }
        public string? Keywords { get; set; }
    }
}
