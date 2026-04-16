namespace MemoryBank.Web.Services;

public record GraphRequest(
    HashSet<EdgeType> EdgeTypes,
    List<string>? CategoryPaths,
    List<string>? Tags,
    List<string>? Types,
    bool IncludeArchived,
    float SimilarityThreshold,
    int SimilarityTopK,
    float TagJaccardMin,
    int Limit);

public enum EdgeType
{
    Links,
    Similarity,
    Tags,
    Category
}

public record GraphNode(
    string Id,
    string Label,
    string Type,
    string? CategoryId,
    string? CategoryPath,
    List<string> Tags,
    int Priority,
    bool Pinned,
    int AccessCount,
    DateTime CreatedAt);

public record GraphEdge(
    string Source,
    string Target,
    string Type,
    float Weight,
    string? LinkType = null);

public record GraphResponse(
    List<GraphNode> Nodes,
    List<GraphEdge> Edges,
    GraphTruncation Truncation);

public record GraphTruncation(int Total, int Shown);

public record FilterOptions(
    List<CategoryOption> Categories,
    List<TagOption> Tags,
    List<string> Types,
    int MemoryCount,
    bool EmbeddingsAvailable);

public record CategoryOption(string Id, string Path, string Name, int MemoryCount);

public record TagOption(string Name, int Count);

public record MemoryDetail(
    string Id,
    string Content,
    string? Summary,
    string Type,
    string? CategoryPath,
    List<string> Tags,
    int Priority,
    bool Pinned,
    bool Archived,
    int AccessCount,
    int RevisionNumber,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? LastAccessed,
    List<LinkedMemory> LinkedMemories,
    List<RevisionSummary> Revisions,
    List<ChunkSummary> Chunks);

public record LinkedMemory(string Id, string Label, string LinkType, string Direction);

public record RevisionSummary(int Number, string? Reason, DateTime CreatedAt, string ContentPreview);

public record ChunkSummary(string Id, int Index, string? Summary, string ContentPreview);

/// <summary>
/// A search hit for the graph viewer.
/// - <see cref="MatchScore"/>: pure content match (0..1), best of keyword vs vector similarity.
///   This is what drives node sizing — it reflects how well the text matches, without
///   ranking noise from priority/pin/recency.
/// - <see cref="VectorScore"/> / <see cref="KeywordScore"/>: individual channel scores for
///   diagnostics.
/// </summary>
public record SearchHit(string Id, float MatchScore, float VectorScore, float KeywordScore);
