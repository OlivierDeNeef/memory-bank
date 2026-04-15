using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DeepMind.Core.Configuration;
using DeepMind.Core.Embeddings;
using DeepMind.Core.Search;
using DeepMind.Core.Storage;
using DeepMind.Server;

var useHttp = args.Contains("--http")
    || Environment.GetEnvironmentVariable("DEEPMIND_TRANSPORT") == "http";

if (useHttp)
{
    await RunHttpServer(args);
}
else
{
    await RunStdioServer(args);
}

static void RegisterServices(IServiceCollection services, IConfiguration configuration)
{
    var config = new DeepMindConfiguration();
    configuration.GetSection("DeepMind").Bind(config);

    // Ensure directories exist
    var deepMindDir = Path.GetDirectoryName(config.Database.Path)!;
    Directory.CreateDirectory(deepMindDir);
    Directory.CreateDirectory(config.Backup.Path);
    Directory.CreateDirectory(Path.GetDirectoryName(config.Logging.FilePath)!);

    services.AddSingleton(config);
    services.AddSingleton(config.Embedding);
    services.AddSingleton(config.Search);
    services.AddSingleton<DeepMindDb>();
    services.AddSingleton<MemoryStore>();
    services.AddSingleton<OnnxEmbeddingService>();
    services.AddSingleton<ChunkingService>();
    services.AddSingleton<HybridSearchEngine>();
    services.AddSingleton<BackupService>();
}

static void RunAutoBackup(IServiceProvider services)
{
    var backupService = services.GetRequiredService<BackupService>();
    if (backupService.IsBackupDue())
    {
        try
        {
            backupService.CreateBackup();
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogWarning(ex, "Auto-backup failed on startup");
        }
    }
}

static async Task RunStdioServer(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    RegisterServices(builder.Services, builder.Configuration);

    // Configure logging to stderr (stdout is reserved for MCP protocol)
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);

    builder.Services.AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    var app = builder.Build();
    RunAutoBackup(app.Services);
    await app.RunAsync();
}

static async Task RunHttpServer(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

    RegisterServices(builder.Services, builder.Configuration);

    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Information);

    builder.Services.AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();

    var app = builder.Build();
    RunAutoBackup(app.Services);

    app.MapOAuth("/mcp");
    app.MapMcp("/mcp");
    app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

    await app.RunAsync();
}
