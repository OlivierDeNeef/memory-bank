using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MemoryBank.Core.Auth;
using MemoryBank.Core.Configuration;
using MemoryBank.Core.Embeddings;
using MemoryBank.Core.Search;
using MemoryBank.Core.Storage;
using MemoryBank.Server;
using MemoryBank.Server.Auth;

if (args.Length >= 2 && args[0] == "--hash-password")
{
    Console.Out.WriteLine(PasswordHasher.Hash(args[1]));
    return;
}

var useHttp = args.Contains("--http")
    || Environment.GetEnvironmentVariable("MEMORYBANK_TRANSPORT") == "http";

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
    var config = new MemoryBankConfiguration();
    configuration.GetSection("MemoryBank").Bind(config);

    // Ensure directories exist
    var dbDir = Path.GetDirectoryName(config.Database.Path)!;
    Directory.CreateDirectory(dbDir);
    Directory.CreateDirectory(config.Backup.Path);
    Directory.CreateDirectory(Path.GetDirectoryName(config.Logging.FilePath)!);

    services.AddSingleton(config);
    services.AddSingleton(config.Embedding);
    services.AddSingleton(config.Search);
    services.AddSingleton<MemoryBankDb>();
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
    var credentials = EnvCredentialValidator.FromEnvironment();

    var builder = WebApplication.CreateBuilder(args);

    RegisterServices(builder.Services, builder.Configuration);
    builder.Services.AddSingleton<OAuthStore>();
    builder.Services.AddSingleton<ICredentialValidator>(credentials);

    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        // Trust forwarded headers from any source. The container ports are bound on the
        // docker host and only nginx (on the same host) reaches them — public traffic
        // is firewalled. Opening this up so the OAuth metadata uses the real https scheme
        // and host that the client sees.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
    });

    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Information);

    builder.Services.AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();

    var app = builder.Build();
    RunAutoBackup(app.Services);

    app.UseForwardedHeaders();
    app.UseBearerTokenAuth("/mcp");

    app.MapOAuth("/mcp");
    app.MapMcp("/mcp");
    app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("HTTP server starting with auth enabled (user={Username})", credentials.ConfiguredUsername);

    await app.RunAsync();
}
