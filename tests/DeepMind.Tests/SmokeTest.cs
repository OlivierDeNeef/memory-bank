using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using DeepMind.Core.Configuration;
using DeepMind.Core.Embeddings;
using DeepMind.Core.Models;
using DeepMind.Core.Search;
using DeepMind.Core.Storage;

namespace DeepMind.Tests;

/// <summary>
/// Full stack smoke test: real DB, real embeddings, real search.
/// Verifies the entire system works end-to-end.
/// </summary>
public class SmokeTest : IDisposable
{
    private readonly DeepMindDb _db;
    private readonly MemoryStore _store;
    private readonly OnnxEmbeddingService _embeddings;
    private readonly HybridSearchEngine _search;
    private readonly ChunkingService _chunking;
    private readonly DeepMindConfiguration _config;

    public SmokeTest()
    {
        _config = new DeepMindConfiguration();
        _config.Database.Path = Path.Combine(Path.GetTempPath(), $"deepmind_smoke_{Guid.NewGuid()}.db");
        _db = new DeepMindDb(_config, NullLogger<DeepMindDb>.Instance);
        _store = new MemoryStore(_db, _config, NullLogger<MemoryStore>.Instance);
        _chunking = new ChunkingService(_config.Embedding);
        _embeddings = new OnnxEmbeddingService(_config.Embedding, NullLogger<OnnxEmbeddingService>.Instance);
        _search = new HybridSearchEngine(_store, _embeddings, _db, _config.Search, NullLogger<HybridSearchEngine>.Instance);
    }

    public void Dispose()
    {
        _embeddings.Dispose();
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_config.Database.Path); } catch { }
        try { File.Delete(_config.Database.Path + "-wal"); } catch { }
        try { File.Delete(_config.Database.Path + "-shm"); } catch { }
    }

    [Fact]
    public void Embedding_Model_Is_Available()
    {
        Assert.True(_embeddings.IsAvailable, "ONNX embedding model should be loaded from bundled files");
        Assert.Equal(768, _embeddings.Dimensions);
    }

    [Fact]
    public void Generate_Embedding_Returns_Valid_Vector()
    {
        var embedding = _embeddings.GenerateEmbedding("Hello world, this is a test");
        Assert.NotNull(embedding);
        Assert.Equal(768, embedding.Length);

        // Should be L2 normalized (magnitude ~1.0)
        var magnitude = MathF.Sqrt(embedding.Sum(x => x * x));
        Assert.InRange(magnitude, 0.99f, 1.01f);
    }

    [Fact]
    public void Similar_Texts_Have_Higher_Cosine_Similarity()
    {
        var embAuth1 = _embeddings.GenerateEmbedding("authentication uses JWT tokens");
        var embAuth2 = _embeddings.GenerateEmbedding("login system with JSON web tokens");
        var embUnrelated = _embeddings.GenerateEmbedding("the weather is sunny today");

        Assert.NotNull(embAuth1);
        Assert.NotNull(embAuth2);
        Assert.NotNull(embUnrelated);

        var similarScore = OnnxEmbeddingService.CosineSimilarity(embAuth1, embAuth2);
        var unrelatedScore = OnnxEmbeddingService.CosineSimilarity(embAuth1, embUnrelated);

        // Similar texts should score higher than unrelated
        Assert.True(similarScore > unrelatedScore,
            $"Similar texts ({similarScore:F3}) should score higher than unrelated ({unrelatedScore:F3})");
    }

    [Fact]
    public void Full_RoundTrip_Store_And_Semantic_Recall()
    {
        // Store several memories
        StoreMemory("The authentication service uses JWT with ES256 signing for all API endpoints",
            "architecture/auth", ["auth", "jwt", "security"], MemoryType.Decision, 4);

        StoreMemory("Database backups run every night at 2 AM via a cron job on the prod server",
            "operations/backup", ["database", "backup", "cron"], MemoryType.Procedure, 3);

        StoreMemory("The frontend uses React 18 with TypeScript and Tailwind CSS",
            "architecture/frontend", ["react", "typescript", "frontend"], MemoryType.Fact, 3);

        StoreMemory("We chose PostgreSQL over MongoDB because we need ACID transactions for billing",
            "architecture/database", ["database", "postgresql"], MemoryType.Decision, 5);

        // Semantic search: query doesn't match exact keywords but should find related content
        var response = _search.Search(new SearchRequest { Query = "how do we handle login tokens", Limit = 5 });
        Assert.True(response.TotalCount > 0, "Should find results for semantic query");

        // The auth/JWT memory should be among the results
        var hasAuthResult = response.Results.Any(r => r.Memory.Content.Contains("JWT"));
        Assert.True(hasAuthResult, "Auth/JWT memory should appear in semantic search results for 'login tokens'");
        Assert.True(response.Results[0].VectorScore > 0, "Vector score should be positive for semantic match");

        // Search for database-related content
        var dbResponse = _search.Search(new SearchRequest { Query = "which database engine did we pick", Limit = 5 });
        Assert.True(dbResponse.TotalCount > 0);
        Assert.Contains("PostgreSQL", dbResponse.Results[0].Memory.Content);
    }

    [Fact]
    public void Full_RoundTrip_Update_And_Revision_History()
    {
        var id = StoreMemory("API rate limit is 100 requests per minute",
            "architecture/api", ["api", "rate-limit"], MemoryType.Fact, 3);

        // Update the fact
        _store.Update(id, "API rate limit is 1000 requests per minute",
            null, 4, null, null, null, null, "increased rate limit after scaling");

        // Verify current state
        var current = _store.GetById(id)!;
        Assert.Equal("API rate limit is 1000 requests per minute", current.Content);
        Assert.Equal(4, current.Priority);
        Assert.Equal(2, current.RevisionNumber);

        // Verify revision history
        var revisions = _store.GetRevisions(id);
        Assert.Single(revisions);
        Assert.Equal("API rate limit is 100 requests per minute", revisions[0].Content);
        Assert.Equal("increased rate limit after scaling", revisions[0].Reason);
    }

    [Fact]
    public void Full_RoundTrip_Category_And_Tag_Filtering()
    {
        StoreMemory("Backend uses .NET 8", "projects/backend", ["dotnet"], MemoryType.Fact, 3);
        StoreMemory("Frontend uses Next.js", "projects/frontend", ["nextjs"], MemoryType.Fact, 3);
        StoreMemory("Backend API is REST", "projects/backend", ["api", "rest"], MemoryType.Fact, 3);

        // Category filter
        var backendResults = _search.Search(new SearchRequest
        {
            Query = "technology stack",
            Category = "projects/backend",
            Limit = 10
        });
        Assert.All(backendResults.Results, r =>
            Assert.StartsWith("projects/backend", r.Memory.CategoryPath ?? ""));

        // Tag filter
        var tagResults = _search.Search(new SearchRequest
        {
            Query = "technology",
            Tags = ["dotnet"],
            Limit = 10
        });
        Assert.All(tagResults.Results, r => Assert.Contains("dotnet", r.Memory.Tags));
    }

    [Fact]
    public void Full_RoundTrip_Large_Content_Chunking()
    {
        // Generate large content (simulating a conversation transcript)
        var paragraphs = Enumerable.Range(1, 20).Select(i =>
            $"In section {i}, we discussed the architecture of the new microservices platform. " +
            $"The team decided to use Kubernetes for orchestration and Istio for service mesh. " +
            $"Performance testing showed that the new design handles {i * 1000} requests per second.");
        var largeContent = string.Join("\n\n", paragraphs);

        var id = StoreMemory(largeContent, "sessions/architecture", ["architecture", "kubernetes"], MemoryType.Reference, 3);

        // Verify chunking happened
        var chunkCount = _store.GetChunkCount(id);
        Assert.True(chunkCount > 1, $"Large content should be chunked, got {chunkCount} chunks");

        // Search should find a specific chunk
        var response = _search.Search(new SearchRequest { Query = "Kubernetes orchestration service mesh", Limit = 5 });
        Assert.True(response.TotalCount > 0);
        Assert.True(response.Results[0].IsChunked, "Result should indicate it's from a chunked memory");
    }

    [Fact]
    public void Health_Check_Reports_Healthy()
    {
        var integrity = _db.RunIntegrityCheck();
        Assert.Equal("ok", integrity);

        var stats = _store.GetStats();
        Assert.True((int)stats["totalMemories"] >= 0);
    }

    [Fact]
    public void Backup_And_Stats()
    {
        StoreMemory("Test memory for backup", null, [], MemoryType.Fact, 3);

        var backupService = new BackupService(_config, NullLogger<BackupService>.Instance);
        var backupPath = backupService.CreateBackup();
        Assert.True(File.Exists(backupPath));

        var stats = _store.GetStats();
        Assert.Equal(1, (int)stats["totalMemories"]);

        // Cleanup
        try { File.Delete(backupPath); } catch { }
    }

    private string StoreMemory(string content, string? category, List<string> tags, MemoryType type, int priority)
    {
        var memory = new Memory
        {
            Content = content,
            Summary = content.Length > 500 ? content[..500] : content,
            Type = type,
            Priority = priority,
            Tags = tags,
            Source = "smoke-test",
            TokenCount = _chunking.EstimateTokenCount(content)
        };

        if (category != null)
            memory.CategoryId = _store.EnsureCategoryPath(category);

        var chunks = _chunking.ChunkText(content, memory.Id);
        _store.Insert(memory, chunks);

        // Generate and store embeddings
        foreach (var chunk in chunks)
        {
            var embedding = _embeddings.GenerateEmbedding(chunk.Content);
            if (embedding != null)
                _store.InsertEmbedding(chunk.Id, memory.Id, embedding, _config.Embedding.ModelName);
        }

        return memory.Id;
    }
}
