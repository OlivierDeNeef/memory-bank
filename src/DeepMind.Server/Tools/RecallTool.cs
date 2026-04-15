using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using DeepMind.Core.Models;
using DeepMind.Core.Search;
using DeepMind.Core.Storage;

namespace DeepMind.Server.Tools;

[McpServerToolType]
public class RecallTool
{
    private readonly HybridSearchEngine _search;
    private readonly MemoryStore _store;

    public RecallTool(HybridSearchEngine search, MemoryStore store)
    {
        _search = search;
        _store = store;
    }

    [McpServerTool(Name = "recall"), Description("IMPORTANT: Do NOT call this tool directly. Use the deepmind:recall skill instead, which handles subagent delegation and clean output formatting. --- Search and recall memories using hybrid search (keyword + semantic + priority). Returns ranked results.")]
    public string Recall(
        [Description("Search query text")] string query,
        [Description("Filter by category path")] string? category = null,
        [Description("Filter by tags (comma-separated)")] string? tags = null,
        [Description("Tag filter mode: 'and' or 'or'")] string tagMode = "or",
        [Description("Minimum priority (1-5)")] int? minPriority = null,
        [Description("Filter by type: fact, decision, procedure, reference, observation")] string? type = null,
        [Description("Filter from date (ISO 8601)")] string? dateFrom = null,
        [Description("Filter to date (ISO 8601)")] string? dateTo = null,
        [Description("Include archived memories")] bool includeArchived = false,
        [Description("Sort: relevance, date, priority, access_count, revision_count")] string sort = "relevance",
        [Description("Max results (1-100)")] int limit = 10,
        [Description("Skip N results")] int offset = 0)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(query))
            return ToolResponse<object>.Fail(ErrorCodes.ValidationFailed, "Query cannot be empty").ToJson();

        var request = new SearchRequest
        {
            Query = query,
            Category = category,
            Tags = string.IsNullOrWhiteSpace(tags) ? null : tags.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList(),
            TagMode = tagMode,
            MinPriority = minPriority,
            Type = type != null ? Enum.Parse<MemoryType>(type, ignoreCase: true) : null,
            DateFrom = dateFrom != null ? DateTime.Parse(dateFrom) : null,
            DateTo = dateTo != null ? DateTime.Parse(dateTo) : null,
            IncludeArchived = includeArchived,
            Sort = sort,
            Limit = Math.Clamp(limit, 1, 100),
            Offset = Math.Max(0, offset)
        };

        var searchResponse = _search.Search(request);

        sw.Stop();

        var results = searchResponse.Results.Select(r => FormatResult(r)).ToList();

        var response = ToolResponse<object>.Ok(new
        {
            results,
            totalCount = searchResponse.TotalCount,
            hasMore = searchResponse.HasMore
        });
        response.Meta.DurationMs = sw.ElapsedMilliseconds;

        return response.ToJson();
    }

    [McpServerTool(Name = "recall_recent"), Description("IMPORTANT: Do NOT call this tool directly. Use the deepmind:recall skill instead, which handles subagent delegation and clean output formatting. --- Recall memories stored within a recent time window.")]
    public string RecallRecent(
        [Description("Hours to look back (default 24)")] int hoursBack = 24,
        [Description("Filter by category path")] string? category = null)
    {
        // Use date-filtered search with a broad query via the store directly
        var request = new SearchRequest
        {
            Query = "",
            Category = category,
            DateFrom = DateTime.UtcNow.AddHours(-hoursBack),
            Sort = "date",
            Limit = 50
        };

        var searchResponse = _search.SearchRecent(request);

        return ToolResponse<object>.Ok(new
        {
            results = searchResponse.Results.Select(r => FormatResult(r)).ToList(),
            totalCount = searchResponse.TotalCount,
            hasMore = searchResponse.HasMore
        }).ToJson();
    }

    [McpServerTool(Name = "exists"), Description("Check if any memories match a query. Returns true/false.")]
    public string Exists(
        [Description("Search query")] string query,
        [Description("Filter by category")] string? category = null)
    {
        var exists = _search.ExistsSearch(query, category);
        return ToolResponse<object>.Ok(new { exists }).ToJson();
    }

    [McpServerTool(Name = "count"), Description("Count memories matching filters without returning content.")]
    public string Count(
        [Description("Search query (optional)")] string? query = null,
        [Description("Filter by category")] string? category = null,
        [Description("Filter by tags (comma-separated)")] string? tags = null,
        [Description("Filter by type")] string? type = null)
    {
        var tagList = string.IsNullOrWhiteSpace(tags) ? null :
            tags.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        var memType = type != null ? Enum.Parse<MemoryType>(type, ignoreCase: true) : (MemoryType?)null;

        var count = _search.CountSearch(query, category, tagList, memType);
        return ToolResponse<object>.Ok(new { count }).ToJson();
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
                ["type"] = r.Memory.Type.ToString().ToLowerInvariant(),
                ["priority"] = r.Memory.Priority,
                ["tags"] = r.Memory.Tags,
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
            ["type"] = r.Memory.Type.ToString().ToLowerInvariant(),
            ["priority"] = r.Memory.Priority,
            ["tags"] = r.Memory.Tags,
            ["isPinned"] = r.Memory.IsPinned,
            ["accessCount"] = r.Memory.AccessCount,
            ["revisionNumber"] = r.Memory.RevisionNumber,
            ["createdAt"] = r.Memory.CreatedAt.ToString("o"),
            ["updatedAt"] = r.Memory.UpdatedAt.ToString("o"),
            ["score"] = MathF.Round(r.FinalScore, 4),
            ["isChunked"] = false,
            ["freshness"] = r.Freshness,
            ["scoreBreakdown"] = new { vector = r.VectorScore, keyword = r.KeywordScore, priority = r.PriorityScore }
        };
    }
}
