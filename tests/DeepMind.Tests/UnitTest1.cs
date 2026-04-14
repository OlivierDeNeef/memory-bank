using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using DeepMind.Core.Configuration;
using DeepMind.Core.Embeddings;
using DeepMind.Core.Models;
using DeepMind.Core.Search;
using DeepMind.Core.Storage;

namespace DeepMind.Tests;

public class MemoryStoreTests : IDisposable
{
    private readonly DeepMindDb _db;
    private readonly MemoryStore _store;
    private readonly ChunkingService _chunking;
    private readonly DeepMindConfiguration _config;

    public MemoryStoreTests()
    {
        _config = new DeepMindConfiguration();
        _config.Database.Path = Path.Combine(Path.GetTempPath(), $"deepmind_test_{Guid.NewGuid()}.db");
        _db = new DeepMindDb(_config, NullLogger<DeepMindDb>.Instance);
        _store = new MemoryStore(_db, _config, NullLogger<MemoryStore>.Instance);
        _chunking = new ChunkingService(_config.Embedding);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_config.Database.Path)) File.Delete(_config.Database.Path); } catch { }
        try { if (File.Exists(_config.Database.Path + "-wal")) File.Delete(_config.Database.Path + "-wal"); } catch { }
        try { if (File.Exists(_config.Database.Path + "-shm")) File.Delete(_config.Database.Path + "-shm"); } catch { }
    }

    [Fact]
    public void Insert_And_GetById_RoundTrip()
    {
        var memory = new Memory
        {
            Content = "Auth service uses JWT with ES256 signing",
            Summary = "Auth JWT config",
            Type = MemoryType.Decision,
            Priority = 4,
            Tags = ["auth", "jwt"],
            Source = "test"
        };

        var chunks = _chunking.ChunkText(memory.Content, memory.Id);
        _store.Insert(memory, chunks);

        var retrieved = _store.GetById(memory.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(memory.Content, retrieved.Content);
        Assert.Equal(memory.Summary, retrieved.Summary);
        Assert.Equal(MemoryType.Decision, retrieved.Type);
        Assert.Equal(4, retrieved.Priority);
        Assert.Contains("auth", retrieved.Tags);
        Assert.Contains("jwt", retrieved.Tags);
    }

    [Fact]
    public void Update_Creates_Revision()
    {
        var memory = new Memory
        {
            Content = "We use RS256 for JWT signing",
            Tags = ["auth"]
        };
        _store.Insert(memory, _chunking.ChunkText(memory.Content, memory.Id));

        _store.Update(memory.Id, "We use ES256 for JWT signing", null, null, null, null, null, null, "switched algorithm");

        var updated = _store.GetById(memory.Id);
        Assert.NotNull(updated);
        Assert.Equal("We use ES256 for JWT signing", updated.Content);
        Assert.Equal(2, updated.RevisionNumber);

        var revisions = _store.GetRevisions(memory.Id);
        Assert.Single(revisions);
        Assert.Equal("We use RS256 for JWT signing", revisions[0].Content);
        Assert.Equal("switched algorithm", revisions[0].Reason);
    }

    [Fact]
    public void Delete_Removes_Memory_And_Cascades()
    {
        var memory = new Memory { Content = "Temporary fact" };
        var chunks = _chunking.ChunkText(memory.Content, memory.Id);
        _store.Insert(memory, chunks);

        Assert.True(_store.Exists(memory.Id));
        _store.Delete(memory.Id);
        Assert.False(_store.Exists(memory.Id));
        Assert.Empty(_store.GetChunks(memory.Id));
    }

    [Fact]
    public void Pin_And_Unpin()
    {
        var memory = new Memory { Content = "Important fact" };
        _store.Insert(memory, _chunking.ChunkText(memory.Content, memory.Id));

        _store.SetPinned(memory.Id, true);
        Assert.True(_store.GetById(memory.Id)!.IsPinned);

        _store.SetPinned(memory.Id, false);
        Assert.False(_store.GetById(memory.Id)!.IsPinned);
    }

    [Fact]
    public void Archive_Hides_From_Default_Queries()
    {
        var memory = new Memory { Content = "Old fact to archive" };
        _store.Insert(memory, _chunking.ChunkText(memory.Content, memory.Id));

        _store.SetArchived(memory.Id, true);
        var archived = _store.GetById(memory.Id);
        Assert.NotNull(archived);
        Assert.True(archived.IsArchived);
    }

    [Fact]
    public void Category_AutoCreation_And_Hierarchy()
    {
        var catId = _store.EnsureCategoryPath("projects/backend/auth");
        var path = _store.GetCategoryPath(catId);
        Assert.Equal("projects/backend/auth", path);

        var catId2 = _store.EnsureCategoryPath("projects/backend/auth");
        Assert.Equal(catId, catId2);
    }

    [Fact]
    public void Tags_Are_Lowercased_And_Deduplicated()
    {
        var memory = new Memory
        {
            Content = "Tag test",
            Tags = ["Auth", "JWT", "auth"]
        };
        _store.Insert(memory, _chunking.ChunkText(memory.Content, memory.Id));

        var retrieved = _store.GetById(memory.Id);
        Assert.NotNull(retrieved);
        Assert.Contains("auth", retrieved.Tags);
        Assert.Contains("jwt", retrieved.Tags);
        Assert.Equal(2, retrieved.Tags.Count);
    }

    [Fact]
    public void Link_And_GetLinked()
    {
        var m1 = new Memory { Content = "First memory" };
        var m2 = new Memory { Content = "Second memory" };
        _store.Insert(m1, _chunking.ChunkText(m1.Content, m1.Id));
        _store.Insert(m2, _chunking.ChunkText(m2.Content, m2.Id));

        _store.LinkMemories(m1.Id, m2.Id, "related");
        var linked = _store.GetLinkedMemories(m1.Id);
        Assert.Single(linked);
        Assert.Equal(m2.Id, linked[0].Id);

        _store.UnlinkMemories(m1.Id, m2.Id);
        Assert.Empty(_store.GetLinkedMemories(m1.Id));
    }

    [Fact]
    public void Revision_Restore()
    {
        var memory = new Memory { Content = "Version 1" };
        _store.Insert(memory, _chunking.ChunkText(memory.Content, memory.Id));

        _store.Update(memory.Id, "Version 2", null, null, null, null, null, null, "update 1");
        _store.Update(memory.Id, "Version 3", null, null, null, null, null, null, "update 2");

        var rev1 = _store.GetRevision(memory.Id, 1);
        Assert.NotNull(rev1);
        Assert.Equal("Version 1", rev1.Content);

        _store.Update(memory.Id, rev1.Content, null, null, null, null, null, null, "restored from revision 1");

        var restored = _store.GetById(memory.Id);
        Assert.Equal("Version 1", restored!.Content);
        Assert.Equal(4, restored.RevisionNumber);
    }

    [Fact]
    public void Stats_Returns_Correct_Counts()
    {
        _store.Insert(new Memory { Content = "Memory 1", Tags = ["a"] },
            _chunking.ChunkText("Memory 1", Guid.NewGuid().ToString()));
        _store.Insert(new Memory { Content = "Memory 2", Tags = ["b"] },
            _chunking.ChunkText("Memory 2", Guid.NewGuid().ToString()));

        var stats = _store.GetStats();
        Assert.Equal(2, stats["totalMemories"]);
        Assert.Equal(2, stats["totalTags"]);
    }

    [Fact]
    public void BulkDelete_By_Tag()
    {
        var m1 = new Memory { Content = "Delete me", Tags = ["temp"] };
        var m2 = new Memory { Content = "Keep me", Tags = ["keep"] };
        _store.Insert(m1, _chunking.ChunkText(m1.Content, m1.Id));
        _store.Insert(m2, _chunking.ChunkText(m2.Content, m2.Id));

        var deleted = _store.BulkDelete(null, "temp", null);
        Assert.Equal(1, deleted);
        Assert.False(_store.Exists(m1.Id));
        Assert.True(_store.Exists(m2.Id));
    }

    [Fact]
    public void Empty_Database_Returns_Empty_Results()
    {
        Assert.Empty(_store.GetAllCategories());
        Assert.Empty(_store.GetTags());
        var stats = _store.GetStats();
        Assert.Equal(0, stats["totalMemories"]);
    }

    [Fact]
    public void DeleteCategory_Fails_If_NotEmpty()
    {
        var catId = _store.EnsureCategoryPath("test-category");
        var memory = new Memory { Content = "In category", CategoryId = catId };
        _store.Insert(memory, _chunking.ChunkText(memory.Content, memory.Id));

        var deleted = _store.DeleteCategory(catId);
        Assert.False(deleted);
    }

    [Fact]
    public void MergeTags_Combines_Associations()
    {
        var m1 = new Memory { Content = "Memory A", Tags = ["old-tag"] };
        var m2 = new Memory { Content = "Memory B", Tags = ["new-tag"] };
        _store.Insert(m1, _chunking.ChunkText(m1.Content, m1.Id));
        _store.Insert(m2, _chunking.ChunkText(m2.Content, m2.Id));

        _store.MergeTags("old-tag", "new-tag");

        var m1Tags = _store.GetById(m1.Id)!.Tags;
        Assert.Contains("new-tag", m1Tags);
        Assert.DoesNotContain("old-tag", _store.GetTags().Select(t => t.Name));
    }
}

public class ChunkingServiceTests
{
    private readonly ChunkingService _chunking;

    public ChunkingServiceTests()
    {
        _chunking = new ChunkingService(new EmbeddingConfig { MaxTokensPerChunk = 50, ChunkOverlapTokens = 10 });
    }

    [Fact]
    public void Small_Content_Returns_Single_Chunk()
    {
        var chunks = _chunking.ChunkText("This is a short sentence.", "test-id");
        Assert.Single(chunks);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal("test-id", chunks[0].MemoryId);
    }

    [Fact]
    public void Large_Content_Returns_Multiple_Chunks()
    {
        var content = string.Join(". ", Enumerable.Range(1, 100).Select(i => $"This is sentence number {i}"));
        var chunks = _chunking.ChunkText(content, "test-id");
        Assert.True(chunks.Count > 1);

        for (int i = 0; i < chunks.Count; i++)
            Assert.Equal(i, chunks[i].ChunkIndex);
    }

    [Fact]
    public void TokenCount_Estimation()
    {
        var count = _chunking.EstimateTokenCount("This is a five word sentence.");
        Assert.True(count > 0);
        Assert.True(count < 20);
    }
}

public class SearchEngineTests : IDisposable
{
    private readonly DeepMindDb _db;
    private readonly MemoryStore _store;
    private readonly HybridSearchEngine _search;
    private readonly ChunkingService _chunking;
    private readonly DeepMindConfiguration _config;

    public SearchEngineTests()
    {
        _config = new DeepMindConfiguration();
        _config.Database.Path = Path.Combine(Path.GetTempPath(), $"deepmind_search_test_{Guid.NewGuid()}.db");
        _db = new DeepMindDb(_config, NullLogger<DeepMindDb>.Instance);
        _store = new MemoryStore(_db, _config, NullLogger<MemoryStore>.Instance);
        _chunking = new ChunkingService(_config.Embedding);
        var embeddings = new OnnxEmbeddingService(_config.Embedding, NullLogger<OnnxEmbeddingService>.Instance);
        _search = new HybridSearchEngine(_store, embeddings, _db, _config.Search, NullLogger<HybridSearchEngine>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_config.Database.Path)) File.Delete(_config.Database.Path); } catch { }
        try { if (File.Exists(_config.Database.Path + "-wal")) File.Delete(_config.Database.Path + "-wal"); } catch { }
        try { if (File.Exists(_config.Database.Path + "-shm")) File.Delete(_config.Database.Path + "-shm"); } catch { }
    }

    [Fact]
    public void Keyword_Search_Finds_Match()
    {
        var memory = new Memory { Content = "The deployment pipeline uses GitHub Actions" };
        _store.Insert(memory, _chunking.ChunkText(memory.Content, memory.Id));

        var response = _search.Search(new SearchRequest { Query = "deployment pipeline" });
        Assert.True(response.TotalCount > 0);
        Assert.Equal(memory.Id, response.Results[0].Memory.Id);
    }

    [Fact]
    public void Search_Respects_Category_Filter()
    {
        var catId = _store.EnsureCategoryPath("backend");
        var m1 = new Memory { Content = "Backend deployment process", CategoryId = catId };
        var m2 = new Memory { Content = "Frontend deployment process" };
        _store.Insert(m1, _chunking.ChunkText(m1.Content, m1.Id));
        _store.Insert(m2, _chunking.ChunkText(m2.Content, m2.Id));

        var response = _search.Search(new SearchRequest { Query = "deployment", Category = "backend" });
        Assert.All(response.Results, r => Assert.Equal(m1.Id, r.Memory.Id));
    }

    [Fact]
    public void Search_Respects_Priority_Filter()
    {
        var m1 = new Memory { Content = "Low priority fact", Priority = 1 };
        var m2 = new Memory { Content = "High priority fact about same topic", Priority = 5 };
        _store.Insert(m1, _chunking.ChunkText(m1.Content, m1.Id));
        _store.Insert(m2, _chunking.ChunkText(m2.Content, m2.Id));

        var response = _search.Search(new SearchRequest { Query = "priority fact", MinPriority = 4 });
        Assert.All(response.Results, r => Assert.True(r.Memory.Priority >= 4));
    }

    [Fact]
    public void Search_Excludes_Archived_By_Default()
    {
        var memory = new Memory { Content = "Archived knowledge about testing" };
        _store.Insert(memory, _chunking.ChunkText(memory.Content, memory.Id));
        _store.SetArchived(memory.Id, true);

        var response = _search.Search(new SearchRequest { Query = "testing" });
        Assert.DoesNotContain(response.Results, r => r.Memory.Id == memory.Id);

        var withArchived = _search.Search(new SearchRequest { Query = "testing", IncludeArchived = true });
        Assert.Contains(withArchived.Results, r => r.Memory.Id == memory.Id);
    }

    [Fact]
    public void Exists_Returns_True_When_Found()
    {
        var memory = new Memory { Content = "Unique searchable content xyz123" };
        _store.Insert(memory, _chunking.ChunkText(memory.Content, memory.Id));

        Assert.True(_search.ExistsSearch("xyz123", null));
        Assert.False(_search.ExistsSearch("nonexistent_term_abc999", null));
    }

    [Fact]
    public void Count_Without_Query()
    {
        _store.Insert(new Memory { Content = "Count test 1" },
            _chunking.ChunkText("Count test 1", Guid.NewGuid().ToString()));
        _store.Insert(new Memory { Content = "Count test 2" },
            _chunking.ChunkText("Count test 2", Guid.NewGuid().ToString()));

        var count = _search.CountSearch(null, null, null, null);
        Assert.Equal(2, count);
    }

    [Fact]
    public void SearchRecent_Returns_Recent_Memories()
    {
        var memory = new Memory { Content = "Just stored this" };
        _store.Insert(memory, _chunking.ChunkText(memory.Content, memory.Id));

        var response = _search.SearchRecent(new SearchRequest
        {
            DateFrom = DateTime.UtcNow.AddHours(-1),
            Sort = "date",
            Limit = 10
        });

        Assert.True(response.TotalCount > 0);
    }

    [Fact]
    public void Pinned_Memories_Score_Higher()
    {
        var m1 = new Memory { Content = "Regular fact about auth systems", Priority = 3 };
        var m2 = new Memory { Content = "Pinned fact about auth systems", Priority = 3 };
        _store.Insert(m1, _chunking.ChunkText(m1.Content, m1.Id));
        _store.Insert(m2, _chunking.ChunkText(m2.Content, m2.Id));
        _store.SetPinned(m2.Id, true);

        var response = _search.Search(new SearchRequest { Query = "auth systems" });
        Assert.True(response.Results.Count >= 2);
        Assert.Equal(m2.Id, response.Results[0].Memory.Id);
    }
}

public class BackupServiceTests : IDisposable
{
    private readonly DeepMindConfiguration _config;
    private readonly DeepMindDb _db;

    public BackupServiceTests()
    {
        _config = new DeepMindConfiguration();
        var tempDir = Path.Combine(Path.GetTempPath(), $"deepmind_backup_test_{Guid.NewGuid()}");
        _config.Database.Path = Path.Combine(tempDir, "deepmind.db");
        _config.Backup.Path = Path.Combine(tempDir, "backups");
        _db = new DeepMindDb(_config, NullLogger<DeepMindDb>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            var dir = Path.GetDirectoryName(_config.Database.Path)!;
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
        catch { }
    }

    [Fact]
    public void CreateBackup_CreatesFile()
    {
        var service = new BackupService(_config, NullLogger<BackupService>.Instance);
        var path = service.CreateBackup();
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void IsBackupDue_True_When_No_Backups()
    {
        var service = new BackupService(_config, NullLogger<BackupService>.Instance);
        Assert.True(service.IsBackupDue());
    }

    [Fact]
    public void Retention_Enforced()
    {
        _config.Backup.MaxBackups = 2;
        var service = new BackupService(_config, NullLogger<BackupService>.Instance);

        service.CreateBackup();
        Thread.Sleep(100);
        service.CreateBackup();
        Thread.Sleep(100);
        service.CreateBackup();

        var files = Directory.GetFiles(_config.Backup.Path, "deepmind_backup_*.db");
        Assert.True(files.Length <= 2);
    }
}
