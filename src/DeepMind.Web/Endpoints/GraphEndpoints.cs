using DeepMind.Core.Embeddings;
using DeepMind.Core.Models;
using DeepMind.Core.Search;
using DeepMind.Core.Storage;
using DeepMind.Web.Services;

namespace DeepMind.Web.Endpoints;

public static class GraphEndpoints
{
    public static void MapGraphEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/filters", GetFilters);
        api.MapGet("/graph", GetGraph);
        api.MapGet("/memory/{id}", GetMemoryDetail);
        api.MapGet("/search", Search);
    }

    private static IResult GetFilters(MemoryStore store, OnnxEmbeddingService embeddings)
    {
        var categories = store.GetAllCategories()
            .Select(c => new CategoryOption(c.Id, c.Path, c.Name, c.MemoryCount))
            .OrderBy(c => c.Path)
            .ToList();

        var tags = store.GetTags(sort: "most_used")
            .Select(t => new TagOption(t.Name, t.UsageCount))
            .ToList();

        var types = Enum.GetNames<MemoryType>().Select(n => n.ToLowerInvariant()).ToList();

        var stats = store.GetStats();
        var memoryCount = Convert.ToInt32(stats["totalMemories"]);

        return Results.Ok(new FilterOptions(
            Categories: categories,
            Tags: tags,
            Types: types,
            MemoryCount: memoryCount,
            EmbeddingsAvailable: embeddings.IsAvailable));
    }

    private static IResult GetGraph(
        GraphService graphService,
        string? edgeTypes,
        string? categories,
        string? tags,
        string? types,
        bool includeArchived = false,
        float simThreshold = 0.78f,
        int simTopK = 5,
        float tagJaccardMin = 0.3f,
        int limit = 1000)
    {
        limit = Math.Clamp(limit, 1, 1000);

        var edgeSet = new HashSet<EdgeType>();
        if (!string.IsNullOrWhiteSpace(edgeTypes))
        {
            foreach (var raw in edgeTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Enum.TryParse<EdgeType>(raw, ignoreCase: true, out var parsed))
                    edgeSet.Add(parsed);
            }
        }
        if (edgeSet.Count == 0) edgeSet.Add(EdgeType.Links);

        var request = new GraphRequest(
            EdgeTypes: edgeSet,
            CategoryPaths: SplitCsv(categories),
            Tags: SplitCsv(tags),
            Types: SplitCsv(types),
            IncludeArchived: includeArchived,
            SimilarityThreshold: Math.Clamp(simThreshold, 0f, 1f),
            SimilarityTopK: Math.Clamp(simTopK, 1, 50),
            TagJaccardMin: Math.Clamp(tagJaccardMin, 0f, 1f),
            Limit: limit);

        var response = graphService.Build(request);
        return Results.Ok(response);
    }

    private static IResult GetMemoryDetail(string id, MemoryStore store)
    {
        var memory = store.GetById(id);
        if (memory == null) return Results.NotFound();

        // Outgoing links only for v1. GetLinkedMemories doesn't expose link_type, so we label
        // it generically; richer link metadata is captured in the graph /api/graph response.
        var outgoingLinks = store.GetLinkedMemories(id)
            .Select(m => new LinkedMemory(
                Id: m.Id,
                Label: Trim(m.Summary ?? m.Content, 60),
                LinkType: "related",
                Direction: "out"))
            .ToList();

        var revisions = store.GetRevisions(id)
            .Select(r => new RevisionSummary(
                Number: r.RevisionNumber,
                Reason: r.Reason,
                CreatedAt: r.CreatedAt,
                ContentPreview: Trim(r.Content, 200)))
            .ToList();

        var chunks = store.GetChunks(id)
            .Select(c => new ChunkSummary(
                Id: c.Id,
                Index: c.ChunkIndex,
                Summary: c.Summary,
                ContentPreview: Trim(c.Content, 200)))
            .ToList();

        var detail = new MemoryDetail(
            Id: memory.Id,
            Content: memory.Content,
            Summary: memory.Summary,
            Type: memory.Type.ToString().ToLowerInvariant(),
            CategoryPath: memory.CategoryPath,
            Tags: memory.Tags,
            Priority: memory.Priority,
            Pinned: memory.IsPinned,
            Archived: memory.IsArchived,
            AccessCount: memory.AccessCount,
            RevisionNumber: memory.RevisionNumber,
            CreatedAt: memory.CreatedAt,
            UpdatedAt: memory.UpdatedAt,
            LastAccessed: memory.LastAccessed,
            LinkedMemories: outgoingLinks,
            Revisions: revisions,
            Chunks: chunks);

        return Results.Ok(detail);
    }

    private static IResult Search(string q, HybridSearchEngine search, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(q)) return Results.Ok(Array.Empty<SearchHit>());

        var response = search.Search(new SearchRequest
        {
            Query = q,
            Limit = Math.Clamp(limit, 1, 200),
            IncludeArchived = false
        });

        // MatchScore = max(vector, keyword) gives a pure 0..1 content-match signal, free of
        // the ranking noise (priority, pin bonus, access boost, recency decay) that FinalScore
        // bakes in. The graph viewer uses this to size nodes so "bigger = better text match".
        var hits = response.Results
            .Select(r => new SearchHit(
                Id: r.Memory.Id,
                MatchScore: Math.Max(r.VectorScore, r.KeywordScore),
                VectorScore: r.VectorScore,
                KeywordScore: r.KeywordScore))
            .OrderByDescending(h => h.MatchScore)
            .ToList();

        return Results.Ok(hits);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static List<string>? SplitCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static string Trim(string text, int max)
    {
        text = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }
}
