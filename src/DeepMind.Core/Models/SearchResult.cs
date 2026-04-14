namespace DeepMind.Core.Models;

public class SearchResult
{
    public Memory Memory { get; set; } = null!;
    public float VectorScore { get; set; }
    public float KeywordScore { get; set; }
    public float PriorityScore { get; set; }
    public float FinalScore { get; set; }
    public bool IsChunked { get; set; }
    public string? MatchedChunk { get; set; }
    public int? ChunkIndex { get; set; }
    public int? TotalChunks { get; set; }
    public string Freshness { get; set; } = "fresh";

    public ScoreBreakdown ScoreBreakdown => new()
    {
        Vector = VectorScore,
        Keyword = KeywordScore,
        Priority = PriorityScore
    };
}

public class ScoreBreakdown
{
    public float Vector { get; set; }
    public float Keyword { get; set; }
    public float Priority { get; set; }
}

public class SearchRequest
{
    public string Query { get; set; } = string.Empty;
    public string? Category { get; set; }
    public List<string>? Tags { get; set; }
    public string TagMode { get; set; } = "or";
    public int? MinPriority { get; set; }
    public MemoryType? Type { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public bool IncludeArchived { get; set; }
    public string Sort { get; set; } = "relevance";
    public int Limit { get; set; } = 10;
    public int Offset { get; set; }
}

public class SearchResponse
{
    public List<SearchResult> Results { get; set; } = [];
    public int TotalCount { get; set; }
    public bool HasMore { get; set; }
}
