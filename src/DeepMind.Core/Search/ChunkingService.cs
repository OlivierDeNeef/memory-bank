using DeepMind.Core.Configuration;
using DeepMind.Core.Models;

namespace DeepMind.Core.Search;

public class ChunkingService
{
    private readonly EmbeddingConfig _config;

    public ChunkingService(EmbeddingConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Estimate token count using a simple word-based approximation (~0.75 tokens per word).
    /// </summary>
    public int EstimateTokenCount(string text)
    {
        return (int)(text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length * 1.33);
    }

    /// <summary>
    /// Split text into chunks respecting sentence boundaries where possible.
    /// Each chunk's summary is enriched with parent memory context for better search.
    /// </summary>
    public List<Chunk> ChunkText(string text, string memoryId, MemoryContext? context = null)
    {
        var contextPrefix = BuildContextPrefix(context);
        var tokenCount = EstimateTokenCount(text);

        if (tokenCount <= _config.MaxTokensPerChunk)
        {
            return [new Chunk
            {
                MemoryId = memoryId,
                ChunkIndex = 0,
                Content = text,
                Summary = BuildChunkSummary(text, contextPrefix),
                Keywords = context?.Tags != null ? string.Join(", ", context.Tags) : null,
                TokenCount = tokenCount
            }];
        }

        var chunks = new List<Chunk>();
        var sentences = SplitIntoSentences(text);
        var currentChunk = new List<string>();
        var currentTokens = 0;
        var chunkIndex = 0;
        var overlapBuffer = new List<string>();

        foreach (var sentence in sentences)
        {
            var sentenceTokens = EstimateTokenCount(sentence);

            if (currentTokens + sentenceTokens > _config.MaxTokensPerChunk && currentChunk.Count > 0)
            {
                // Emit chunk
                var chunkContent = string.Join(" ", currentChunk);
                chunks.Add(new Chunk
                {
                    MemoryId = memoryId,
                    ChunkIndex = chunkIndex++,
                    Content = chunkContent,
                    Summary = BuildChunkSummary(chunkContent, contextPrefix),
                    Keywords = context?.Tags != null ? string.Join(", ", context.Tags) : null,
                    TokenCount = currentTokens
                });

                // Keep overlap sentences
                overlapBuffer.Clear();
                var overlapTokens = 0;
                for (int i = currentChunk.Count - 1; i >= 0; i--)
                {
                    var st = EstimateTokenCount(currentChunk[i]);
                    if (overlapTokens + st > _config.ChunkOverlapTokens) break;
                    overlapBuffer.Insert(0, currentChunk[i]);
                    overlapTokens += st;
                }

                currentChunk.Clear();
                currentChunk.AddRange(overlapBuffer);
                currentTokens = overlapTokens;
            }

            currentChunk.Add(sentence);
            currentTokens += sentenceTokens;
        }

        // Final chunk
        if (currentChunk.Count > 0)
        {
            var chunkContent = string.Join(" ", currentChunk);
            chunks.Add(new Chunk
            {
                MemoryId = memoryId,
                ChunkIndex = chunkIndex,
                Content = chunkContent,
                Summary = BuildChunkSummary(chunkContent, contextPrefix),
                Keywords = context?.Tags != null ? string.Join(", ", context.Tags) : null,
                TokenCount = currentTokens
            });
        }

        return chunks;
    }

    /// <summary>
    /// Build a context prefix from memory metadata to prepend to chunk summaries.
    /// This ensures every chunk is findable via the parent memory's key terms.
    /// </summary>
    private static string BuildContextPrefix(MemoryContext? context)
    {
        if (context == null) return "";

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.Summary))
            parts.Add(context.Summary);
        if (!string.IsNullOrWhiteSpace(context.CategoryPath))
            parts.Add(context.CategoryPath);
        if (context.Tags is { Count: > 0 })
            parts.Add(string.Join(", ", context.Tags));

        return parts.Count > 0 ? $"[{string.Join(" | ", parts)}] " : "";
    }

    private static string BuildChunkSummary(string chunkContent, string contextPrefix)
    {
        var maxContentLen = 500 - contextPrefix.Length;
        if (maxContentLen < 100) maxContentLen = 100;
        var contentPart = chunkContent.Length > maxContentLen ? chunkContent[..maxContentLen] : chunkContent;
        return contextPrefix + contentPart;
    }

    private static List<string> SplitIntoSentences(string text)
    {
        var sentences = new List<string>();
        var current = 0;
        var sentenceEnders = new[] { '.', '!', '?', '\n' };

        while (current < text.Length)
        {
            var nextEnd = -1;
            foreach (var ender in sentenceEnders)
            {
                var idx = text.IndexOf(ender, current);
                if (idx >= 0 && (nextEnd < 0 || idx < nextEnd))
                    nextEnd = idx;
            }

            if (nextEnd < 0)
            {
                sentences.Add(text[current..].Trim());
                break;
            }

            var sentence = text[current..(nextEnd + 1)].Trim();
            if (sentence.Length > 0)
                sentences.Add(sentence);
            current = nextEnd + 1;
        }

        return sentences;
    }
}
