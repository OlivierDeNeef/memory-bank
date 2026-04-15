using DeepMind.Core.Embeddings;
using DeepMind.Core.Models;
using DeepMind.Core.Storage;
using Microsoft.Extensions.Logging;

namespace DeepMind.Web.Services;

/// <summary>
/// Builds the node + edge graph for the 3D viewer. Edge types are opt-in via the request.
/// </summary>
public class GraphService
{
    private readonly MemoryStore _store;
    private readonly OnnxEmbeddingService _embeddings;
    private readonly ILogger<GraphService> _logger;

    public GraphService(MemoryStore store, OnnxEmbeddingService embeddings, ILogger<GraphService> logger)
    {
        _store = store;
        _embeddings = embeddings;
        _logger = logger;
    }

    public GraphResponse Build(GraphRequest request)
    {
        // Resolve category filter paths → IDs (including descendants) via MemoryStore
        List<string>? categoryIds = null;
        if (request.CategoryPaths != null && request.CategoryPaths.Count > 0)
        {
            categoryIds = [];
            foreach (var path in request.CategoryPaths)
            {
                var catId = _store.FindCategoryByPath(path);
                if (catId != null)
                    categoryIds.AddRange(_store.GetCategoryAndDescendantIds(catId));
            }
        }

        List<MemoryType>? types = null;
        if (request.Types != null && request.Types.Count > 0)
        {
            types = [];
            foreach (var t in request.Types)
            {
                if (Enum.TryParse<MemoryType>(t, ignoreCase: true, out var parsed))
                    types.Add(parsed);
            }
        }

        var (memories, totalCount) = _store.ListMemories(
            categoryIds: categoryIds,
            tagNames: request.Tags,
            types: types,
            includeArchived: request.IncludeArchived,
            limit: request.Limit);

        var nodes = memories.Select(m => new GraphNode(
            Id: m.Id,
            Label: BuildLabel(m),
            Type: m.Type.ToString().ToLowerInvariant(),
            CategoryId: m.CategoryId,
            CategoryPath: m.CategoryPath,
            Tags: m.Tags,
            Priority: m.Priority,
            Pinned: m.IsPinned,
            AccessCount: m.AccessCount,
            CreatedAt: m.CreatedAt)).ToList();

        var nodeIds = memories.Select(m => m.Id).ToHashSet();
        var edges = new List<GraphEdge>();

        if (request.EdgeTypes.Contains(EdgeType.Links))
            edges.AddRange(BuildLinkEdges(nodeIds));

        if (request.EdgeTypes.Contains(EdgeType.Tags))
            edges.AddRange(BuildTagEdges(memories, request.TagJaccardMin));

        if (request.EdgeTypes.Contains(EdgeType.Category))
            edges.AddRange(BuildCategoryEdges(memories));

        if (request.EdgeTypes.Contains(EdgeType.Similarity))
            edges.AddRange(BuildSimilarityEdges(memories, request.SimilarityThreshold, request.SimilarityTopK));

        return new GraphResponse(
            Nodes: nodes,
            Edges: edges,
            Truncation: new GraphTruncation(Total: totalCount, Shown: nodes.Count));
    }

    // ── Edge builders ────────────────────────────────────────────────

    private List<GraphEdge> BuildLinkEdges(HashSet<string> nodeIds)
    {
        var rows = _store.GetLinksWithin(nodeIds);
        return rows.Select(r => new GraphEdge(
            Source: r.SourceId,
            Target: r.TargetId,
            Type: "link",
            Weight: 1f,
            LinkType: r.LinkType)).ToList();
    }

    private List<GraphEdge> BuildTagEdges(List<Memory> memories, float jaccardMin)
    {
        var edges = new List<GraphEdge>();
        var sets = memories
            .Select(m => (m.Id, Tags: m.Tags.Select(t => t.ToLowerInvariant()).ToHashSet()))
            .Where(t => t.Tags.Count > 0)
            .ToList();

        for (int i = 0; i < sets.Count; i++)
        {
            for (int j = i + 1; j < sets.Count; j++)
            {
                var a = sets[i].Tags;
                var b = sets[j].Tags;
                var intersection = a.Intersect(b).Count();
                if (intersection == 0) continue;
                var union = a.Count + b.Count - intersection;
                var jaccard = (float)intersection / union;
                if (jaccard < jaccardMin) continue;
                edges.Add(new GraphEdge(sets[i].Id, sets[j].Id, "tags", jaccard));
            }
        }
        return edges;
    }

    private static List<GraphEdge> BuildCategoryEdges(List<Memory> memories)
    {
        var edges = new List<GraphEdge>();
        var groups = memories
            .Where(m => m.CategoryId != null)
            .GroupBy(m => m.CategoryId!);

        foreach (var group in groups)
        {
            var items = group.ToList();
            // Connect each pair within the same category. Cheap because a single category
            // rarely holds more than a few dozen memories.
            for (int i = 0; i < items.Count; i++)
            {
                for (int j = i + 1; j < items.Count; j++)
                {
                    edges.Add(new GraphEdge(items[i].Id, items[j].Id, "category", 1f));
                }
            }
        }
        return edges;
    }

    private List<GraphEdge> BuildSimilarityEdges(List<Memory> memories, float threshold, int topK)
    {
        if (!_embeddings.IsAvailable || memories.Count < 2) return [];

        var nodeIds = memories.Select(m => m.Id).ToHashSet();

        // Mean-pool chunk embeddings to a single vector per memory, restricted to our node set.
        var rawEmbeddings = _store.GetAllEmbeddings();
        var perMemory = new Dictionary<string, (float[] Sum, int Count)>();

        foreach (var (_, memoryId, embedding) in rawEmbeddings)
        {
            if (!nodeIds.Contains(memoryId)) continue;
            if (!perMemory.TryGetValue(memoryId, out var entry))
            {
                perMemory[memoryId] = (embedding.ToArray(), 1);
            }
            else
            {
                for (int i = 0; i < entry.Sum.Length; i++)
                    entry.Sum[i] += embedding[i];
                perMemory[memoryId] = (entry.Sum, entry.Count + 1);
            }
        }

        var pooled = new List<(string Id, float[] Vector)>(perMemory.Count);
        foreach (var (id, entry) in perMemory)
        {
            var vec = new float[entry.Sum.Length];
            for (int i = 0; i < vec.Length; i++) vec[i] = entry.Sum[i] / entry.Count;
            Normalize(vec);
            pooled.Add((id, vec));
        }

        if (pooled.Count < 2) return [];

        // For each node keep top-K neighbors above threshold. Track pairs via canonical ordering
        // so we don't double-emit (a→b and b→a).
        var keptPerNode = new Dictionary<string, List<(string Neighbor, float Score)>>();
        foreach (var (id, _) in pooled)
            keptPerNode[id] = new List<(string, float)>(topK);

        for (int i = 0; i < pooled.Count; i++)
        {
            for (int j = i + 1; j < pooled.Count; j++)
            {
                var score = Dot(pooled[i].Vector, pooled[j].Vector);
                if (score < threshold) continue;
                TryInsertTopK(keptPerNode[pooled[i].Id], pooled[j].Id, score, topK);
                TryInsertTopK(keptPerNode[pooled[j].Id], pooled[i].Id, score, topK);
            }
        }

        var seen = new HashSet<(string, string)>();
        var edges = new List<GraphEdge>();
        foreach (var (id, neighbors) in keptPerNode)
        {
            foreach (var (neighborId, score) in neighbors)
            {
                var key = string.CompareOrdinal(id, neighborId) < 0 ? (id, neighborId) : (neighborId, id);
                if (!seen.Add(key)) continue;
                edges.Add(new GraphEdge(key.Item1, key.Item2, "similarity", score));
            }
        }
        return edges;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static string BuildLabel(Memory m)
    {
        if (!string.IsNullOrWhiteSpace(m.Summary)) return Trim(m.Summary!, 80);
        return Trim(m.Content, 80);
    }

    private static string Trim(string text, int max)
    {
        text = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }

    private static void Normalize(float[] v)
    {
        double sum = 0;
        for (int i = 0; i < v.Length; i++) sum += v[i] * v[i];
        var norm = (float)Math.Sqrt(sum);
        if (norm < 1e-8f) return;
        for (int i = 0; i < v.Length; i++) v[i] /= norm;
    }

    private static float Dot(float[] a, float[] b)
    {
        float sum = 0;
        for (int i = 0; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }

    private static void TryInsertTopK(List<(string Neighbor, float Score)> list, string neighbor, float score, int k)
    {
        if (list.Count < k)
        {
            list.Add((neighbor, score));
            return;
        }
        // Find minimum slot
        int minIdx = 0;
        for (int i = 1; i < list.Count; i++)
            if (list[i].Score < list[minIdx].Score) minIdx = i;

        if (score > list[minIdx].Score)
            list[minIdx] = (neighbor, score);
    }
}
