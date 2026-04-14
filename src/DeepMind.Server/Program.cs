using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DeepMind.Core.Configuration;
using DeepMind.Core.Embeddings;
using DeepMind.Core.Search;
using DeepMind.Core.Storage;

var builder = Host.CreateApplicationBuilder(args);

// Load configuration
var config = new DeepMindConfiguration();
builder.Configuration.GetSection("DeepMind").Bind(config);

// Ensure directories exist
var deepMindDir = Path.GetDirectoryName(config.Database.Path)!;
Directory.CreateDirectory(deepMindDir);
Directory.CreateDirectory(config.Backup.Path);
Directory.CreateDirectory(Path.GetDirectoryName(config.Logging.FilePath)!);

// Register services
builder.Services.AddSingleton(config);
builder.Services.AddSingleton(config.Embedding);
builder.Services.AddSingleton(config.Search);
builder.Services.AddSingleton<DeepMindDb>();
builder.Services.AddSingleton<MemoryStore>();
builder.Services.AddSingleton<OnnxEmbeddingService>();
builder.Services.AddSingleton<ChunkingService>();
builder.Services.AddSingleton<HybridSearchEngine>();
builder.Services.AddSingleton<BackupService>();

// Configure logging to stderr (stdout is reserved for MCP protocol)
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// Configure MCP server
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

// Auto-backup on startup if due
var backupService = app.Services.GetRequiredService<BackupService>();
if (backupService.IsBackupDue())
{
    try
    {
        backupService.CreateBackup();
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "Auto-backup failed on startup");
    }
}

await app.RunAsync();
