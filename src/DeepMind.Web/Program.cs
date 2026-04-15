using DeepMind.Core.Configuration;
using DeepMind.Core.Embeddings;
using DeepMind.Core.Search;
using DeepMind.Core.Storage;
using DeepMind.Web.Endpoints;
using DeepMind.Web.Services;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = "wwwroot"
});

// Bind configuration (shares DeepMind section with the MCP server)
var config = new DeepMindConfiguration();
builder.Configuration.GetSection("DeepMind").Bind(config);

Directory.CreateDirectory(Path.GetDirectoryName(config.Database.Path)!);

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(config.Embedding);
builder.Services.AddSingleton(config.Search);
builder.Services.AddSingleton<DeepMindDb>();
builder.Services.AddSingleton<MemoryStore>();
builder.Services.AddSingleton<OnnxEmbeddingService>();
builder.Services.AddSingleton<ChunkingService>();
builder.Services.AddSingleton<HybridSearchEngine>();
builder.Services.AddSingleton<GraphService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

// Order matters: static files must run BEFORE UseRouting so requests for physical files
// (index.html, assets/*) are served directly without being captured by the fallback endpoint.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseCors();

app.MapGraphEndpoints();

// SPA fallback: any unmatched request (e.g. client-side routes) returns index.html.
app.MapFallbackToFile("index.html");

app.Run();
