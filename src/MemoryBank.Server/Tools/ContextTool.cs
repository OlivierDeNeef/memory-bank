using System.ComponentModel;
using ModelContextProtocol.Server;
using MemoryBank.Core.Models;
using MemoryBank.Core.Search;
using MemoryBank.Core.Storage;

namespace MemoryBank.Server.Tools;

[McpServerToolType]
public class ContextTool
{
    private readonly HybridSearchEngine _search;
    private readonly MemoryStore _store;

    public ContextTool(HybridSearchEngine search, MemoryStore store)
    {
        _search = search;
        _store = store;
    }

    [McpServerTool(Name = "recall_context"),
     Description("Recall all memories relevant to a project context. Runs a broad semantic search and always includes pinned memories. Returns results grouped by type (decisions, procedures, references, etc.).")]
    public string RecallContext(
        [Description("Project description: name, tech stack, goals, domain — anything that helps match relevant memories")]
        string description,
        [Description("Additional keywords to search for (comma-separated, optional)")]
        string? keywords = null,
        [Description("Filter by tags (comma-separated, optional)")]
        string? tags = null,
        [Description("Minimum relevance score 0.0-1.0 (default 0.15)")]
        float minScore = 0.15f)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(description))
            return ToolResponse<object>.Fail(ErrorCodes.ValidationFailed, "Description cannot be empty").ToJson();

        var seen = new Dictionary<string, SearchResult>();

        // 1. Main semantic search using the project description
        var mainRequest = new SearchRequest
        {
            Query = description,
            Tags = ParseTags(tags),
            TagMode = "or",
            Sort = "relevance",
            Limit = 50
        };
        MergeResults(seen, _search.Search(mainRequest), minScore);

        // 2. Search additional keywords individually for broader coverage
        if (!string.IsNullOrWhiteSpace(keywords))
        {
            foreach (var keyword in keywords.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = keyword.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                var kwRequest = new SearchRequest
                {
                    Query = trimmed,
                    Tags = ParseTags(tags),
                    TagMode = "or",
                    Sort = "relevance",
                    Limit = 20
                };
                MergeResults(seen, _search.Search(kwRequest), minScore);
            }
        }

        // 3. Always include pinned memories — they're universally important
        var pinned = _store.GetPinnedMemories();
        foreach (var memory in pinned)
        {
            if (!seen.ContainsKey(memory.Id))
            {
                seen[memory.Id] = new SearchResult
                {
                    Memory = memory,
                    FinalScore = 1.0f,
                    PriorityScore = memory.Priority / 5f,
                    IsChunked = _store.GetChunkCount(memory.Id) > 1,
                    Freshness = GetFreshness(memory)
                };
            }
        }

        // 4. Group by type
        var grouped = seen.Values
            .OrderByDescending(r => r.FinalScore)
            .GroupBy(r => r.Memory.Type.ToString().ToLowerInvariant())
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => FormatResult(r)).ToList()
            );

        sw.Stop();

        var response = ToolResponse<object>.Ok(new
        {
            totalRecalled = seen.Count,
            groups = grouped,
            types = grouped.Keys.ToList()
        });
        response.Meta.DurationMs = sw.ElapsedMilliseconds;

        return response.ToJson();
    }

    private static void MergeResults(Dictionary<string, SearchResult> seen, SearchResponse response, float minScore)
    {
        foreach (var result in response.Results)
        {
            if (result.FinalScore < minScore) continue;

            if (!seen.TryGetValue(result.Memory.Id, out var existing) || existing.FinalScore < result.FinalScore)
                seen[result.Memory.Id] = result;
        }
    }

    private static List<string>? ParseTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags)) return null;
        return tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToList();
    }

    private static string GetFreshness(Memory memory)
    {
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

    private static object FormatResult(SearchResult r)
    {
        if (r.IsChunked && r.MatchedChunk != null)
        {
            return new Dictionary<string, object?>
            {
                ["id"] = r.Memory.Id,
                ["matchedChunk"] = r.MatchedChunk,
                ["chunkIndex"] = r.ChunkIndex,
                ["totalChunks"] = r.TotalChunks,
                ["parentSummary"] = r.Memory.Summary,
                ["category"] = r.Memory.CategoryPath,
                ["priority"] = r.Memory.Priority,
                ["tags"] = r.Memory.Tags,
                ["isPinned"] = r.Memory.IsPinned,
                ["score"] = MathF.Round(r.FinalScore, 4),
                ["isChunked"] = true,
                ["freshness"] = r.Freshness,
                ["hint"] = "Use get_memory or get_chunks for full content"
            };
        }

        return new Dictionary<string, object?>
        {
            ["id"] = r.Memory.Id,
            ["content"] = r.Memory.Content,
            ["summary"] = r.Memory.Summary,
            ["category"] = r.Memory.CategoryPath,
            ["priority"] = r.Memory.Priority,
            ["tags"] = r.Memory.Tags,
            ["isPinned"] = r.Memory.IsPinned,
            ["score"] = MathF.Round(r.FinalScore, 4),
            ["isChunked"] = false,
            ["freshness"] = r.Freshness
        };
    }
}
