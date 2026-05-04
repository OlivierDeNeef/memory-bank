using Microsoft.AspNetCore.HttpOverrides;
using MemoryBank.Core.Configuration;
using MemoryBank.Core.Embeddings;
using MemoryBank.Core.Search;
using MemoryBank.Core.Storage;
using MemoryBank.Web.Auth;
using MemoryBank.Web.Endpoints;
using MemoryBank.Web.Services;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = "wwwroot"
});

// Bind configuration (shares MemoryBank section with the MCP server)
var config = new MemoryBankConfiguration();
builder.Configuration.GetSection("MemoryBank").Bind(config);

Directory.CreateDirectory(Path.GetDirectoryName(config.Database.Path)!);

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(config.Embedding);
builder.Services.AddSingleton(config.Search);
builder.Services.AddSingleton<MemoryBankDb>();
builder.Services.AddSingleton<MemoryStore>();
builder.Services.AddSingleton<OnnxEmbeddingService>();
builder.Services.AddSingleton<ChunkingService>();
builder.Services.AddSingleton<HybridSearchEngine>();
builder.Services.AddSingleton<GraphService>();
builder.Services.AddSingleton<OAuthStore>();
builder.Services.AddSingleton<ViewerAuthService>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // Same reasoning as MemoryBank.Server: nginx terminates TLS on the same docker host;
    // only it reaches the bound port. Trust the forwarded scheme/host so cookies set Secure
    // correctly and OAuth redirects use the public https URL.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var app = builder.Build();

app.UseForwardedHeaders();

// Static assets are public (under /assets/, plus favicon). UseStaticFiles before auth so the
// gate doesn't redirect public bundle requests. UseDefaultFiles is OFF: index.html must go
// through the auth gate. We serve index.html via the SPA fallback after authentication.
app.UseStaticFiles();

app.UseRouting();
app.UseCors();

app.UseViewerAuth();

app.MapAuthEndpoints();
app.MapGraphEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// SPA fallback: any unmatched request (e.g. client-side routes) returns index.html.
app.MapFallbackToFile("index.html");

app.Run();
