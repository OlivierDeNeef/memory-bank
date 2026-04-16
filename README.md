# MemoryBank

A persistent, searchable memory system for AI assistants, exposed as an MCP server — plus a 2D force-directed graph viewer for exploring what you've stored.

- **Store**: facts, decisions, procedures, references, observations — tagged, categorized, versioned
- **Recall**: hybrid search (SQLite FTS5 + semantic cosine over local ONNX embeddings)
- **Explore**: web UI that renders your memory as a graph, with edges for explicit links, semantic similarity, tag overlap, and shared categories

Everything runs locally. No external API calls, no cloud services.

---

## Prerequisites

| Required | Why |
|---|---|
| **.NET 10 SDK** | Builds and runs all three projects |
| **Node.js 20+** | Only for the graph viewer's SPA build (`MemoryBank.Web/ClientApp`) |
| **Docker Desktop** (optional) | Easiest way to run everything with one command |
| **Git** | To clone the repo |

You need Node only if you run the web viewer from source. If you run via Docker, Node is bundled in the build image.

---

## One-time setup: download the embedding model

The embedding model is **not** checked into the repo (it's ~520 MB). You must download it before semantic search / similarity edges will work. Keyword search and all other features work without it.

### Model: `nomic-embed-text-v1.5` (ONNX, 768 dimensions)

1. Download `model.onnx` from the official HuggingFace repo:
   <https://huggingface.co/nomic-ai/nomic-embed-text-v1.5/tree/main/onnx>
2. Place it at:
   ```
   src/MemoryBank.Core/Embeddings/Models/model.onnx
   ```

The tokenizer (`vocab.txt`) is already included in the repo.

On first run you'll see either:
- `Embedding model loaded: nomic-embed-text-v1.5 (768d)` — good, vector search is live
- `Embedding model not found. Vector search disabled.` — keyword search still works, but similarity edges in the graph viewer will be empty

### Using a different model

Edit `src/MemoryBank.Server/appsettings.json` (or pass `MemoryBank__Embedding__*` env vars):

```json
{
  "MemoryBank": {
    "Embedding": {
      "ModelPath": "/absolute/path/to/your-model.onnx",
      "ModelName": "your-model-name",
      "Dimensions": 384
    }
  }
}
```

Any embedding model that outputs token-level vectors (BERT-family, mean-pooled) should work. Dimensions must match the model's actual output — mismatch will produce wrong similarity scores.

> **Switching models with existing data**: embeddings stored at the old dimension count are incompatible with the new model. Delete `~/.memorybank/memorybank.db` (or re-embed via the `reembed_all` MCP tool) after switching.

---

## Running it

There are three ways to run MemoryBank. Pick based on what you want to do.

### Option A — Docker (easiest, runs everything)

This spins up both the MCP server (HTTP transport on `:6868`) and the graph viewer (`:5174`), sharing a single SQLite database via a named volume.

```bash
# From the repo root
docker compose up -d
```

Then open:
- Graph viewer: <http://localhost:5174>
- MCP server health: <http://localhost:6868/health>

Stop with:
```bash
docker compose down
```

The database persists in the `memorybank-data` Docker volume across restarts. To wipe it:
```bash
docker compose down -v
```

**Note**: the embedding model needs to be at `src/MemoryBank.Core/Embeddings/Models/model.onnx` *before* you build the Docker image — the `CopyToOutputDirectory` in the csproj bakes it into the image. If it's missing, vector search will be silently disabled inside the container.

### Option B — Run from source (for development)

Three terminals if you want everything, or just the one you need.

**MCP server (stdio — for Claude Code / other MCP clients):**
```bash
dotnet run --project src/MemoryBank.Server
```

**MCP server (HTTP, port 6868):**
```bash
dotnet run --project src/MemoryBank.Server -- --http
```

**Graph viewer (port 5174 backend + 5173 Vite dev server):**
```bash
# Terminal 1 — backend
dotnet run --project src/MemoryBank.Web

# Terminal 2 — frontend with hot-reload
cd src/MemoryBank.Web/ClientApp
npm install       # first time only
npm run dev
```
Open <http://localhost:5173> — Vite proxies `/api/*` to the backend.

For a production-style local run (single process serving both API and prebuilt SPA):
```bash
dotnet publish src/MemoryBank.Web -c Release -o publish/web
dotnet ./publish/web/MemoryBank.Web.dll
# → http://localhost:5174
```

### Option C — Use it with Claude Code

MemoryBank's primary purpose is to be an MCP server for AI assistants. For Claude Code there are **two things to register**:

1. The **MCP server** — so Claude can actually call MemoryBank's tools
2. The **plugin** at `src/MemoryBank.Plugin/` — provides slash commands (`/remember`, `/recall`, `/search`, `/stats`, `/todo`, `/forget`) and a PreToolUse hook that ensures all memory access goes through the skill layer rather than raw MCP tool calls

You can install either independently, but using them together is the intended experience.

#### 1. Register the MCP server

Pick the transport that matches how you're running the server.

**Stdio (recommended for local use)** — Claude Code launches the server process for you:
```bash
# From the repo root, using dotnet run
claude mcp add --transport stdio memorybank -- dotnet run --project ./src/MemoryBank.Server
```

For faster startup (no JIT on each launch), publish first:
```bash
dotnet publish src/MemoryBank.Server -c Release -o publish/server

claude mcp add --transport stdio memorybank -- dotnet ./publish/server/MemoryBank.Server.dll
```

**HTTP (when running via Docker)** — the `memorybank` container already exposes MCP on `:6868`:
```bash
claude mcp add --transport http memorybank http://localhost:6868/mcp
```

**Scope**: add `--scope user` to make the server available in every Claude Code session, or `--scope project` to commit the config to `.mcp.json` so your team picks it up. Default scope is local (only this directory).

Verify it's connected:
```
/mcp
```

#### 2. Install the plugin

**For quick trial** — launch Claude Code with the plugin directory mounted for the current session only:
```bash
claude --plugin-dir ./src/MemoryBank.Plugin
```

**For permanent install** — register the local plugin directory as a marketplace and install from it:
```
/plugin marketplace add ./src/MemoryBank.Plugin
/plugin install memorybank@memorybank-local
/reload-plugins
```

After the reload, `/remember`, `/recall`, `/search`, `/stats`, `/todo`, and `/forget` are available as slash commands. The plugin's PreToolUse hook at `scripts/block-direct-mcp.sh` intercepts any direct `mcp__memorybank__*` calls and nudges Claude to go through the skills instead.

---

## Configuration

Defaults live in `src/MemoryBank.Core/Configuration/MemoryBankConfiguration.cs` and can be overridden via `appsettings.json` or environment variables with the `MemoryBank__` prefix.

| Setting | Default | Env var |
|---|---|---|
| Database path | `~/.memorybank/memorybank.db` | `MemoryBank__Database__Path` |
| Backup path | `~/.memorybank/backups` | `MemoryBank__Backup__Path` |
| Embedding model path | `~/.memorybank/models/nomic-embed-text-v1.5.onnx` | `MemoryBank__Embedding__ModelPath` |
| Embedding dimensions | 768 | `MemoryBank__Embedding__Dimensions` |
| Log file | `~/.memorybank/logs/memorybank.log` | `MemoryBank__Logging__FilePath` |

The model path is also checked relative to the running assembly — which is why the `Models/` subfolder in `MemoryBank.Core` works automatically once you drop the `.onnx` file in.

In the Docker container these defaults are overridden to `/data/memorybank/*` so they land on the mounted volume.

---

## Project layout

```
src/
├── MemoryBank.Core/           Shared library: storage, search, embeddings, config
│   ├── Storage/             SQLite schema, MemoryStore, BackupService
│   ├── Search/              HybridSearchEngine (FTS5 + vector), chunking
│   └── Embeddings/          OnnxEmbeddingService (+ Models/ folder)
├── MemoryBank.Server/         MCP server (stdio + HTTP transports)
│   ├── Tools/               MCP tool handlers (Remember, Recall, Manage, etc.)
│   └── OAuthEndpoints.cs    OAuth for HTTP transport
├── MemoryBank.Web/            Graph viewer (ASP.NET + React SPA)
│   ├── Endpoints/           /api/graph, /api/filters, /api/memory/{id}, /api/search
│   ├── Services/            GraphService (builds nodes + edges)
│   └── ClientApp/           Vite + React + TypeScript + Tailwind
└── MemoryBank.Plugin/         Claude Code plugin with skills (remember, recall, search, ...)

tests/
└── MemoryBank.Tests/          Smoke tests

Dockerfile                   Image for the MCP server (HTTP transport on :6868)
Dockerfile.web               Multi-stage image for the graph viewer (:5174)
docker-compose.yml           Both services sharing a memorybank-data volume
```

---

## The graph viewer at a glance

![edge types: explicit links, semantic similarity, tag overlap, shared category]

Key interactions:

- **Left panel** — filters (multi-select edge types, categories, tags, types) + search box + similarity/tag thresholds. Changes auto-refresh the graph with a short debounce.
- **Search** — live hybrid search. Matching nodes scale up relative to each other; a spinner in the input shows fetch state.
- **Hover/click a node** — labels appear (drawn on top of everything), detail panel opens on the right with content, linked memories, revisions, chunks.
- **Edges** — colored by type, thickness/opacity scale with relationship strength normalized to the threshold sliders so the full visual range is always in use.
- **Pinned memories** — get a yellow halo.

See `src/MemoryBank.Web/README.md` for the web-specific API and dev details.

---

## Troubleshooting

**"Embedding model not found. Vector search disabled."**
→ The model file is missing. See [One-time setup](#one-time-setup-download-the-embedding-model).

**Graph viewer shows HTTP 404 on `/` even though the container is healthy**
→ Check for a leftover `MemoryBank.Web` dev process on `127.0.0.1:5174` — Windows will prefer it over Docker's `0.0.0.0:5174` forward. Kill it and reload.

**Database locked errors**
→ SQLite WAL mode handles concurrent readers fine, but if you see lock errors, check that only one process is writing (the MCP server should be the only writer; the web viewer is read-only).

**Migrations fail on startup**
→ Either a corrupt DB file or a schema mismatch from a previous version. Back up `~/.memorybank/memorybank.db` and delete it to start fresh.

**Port 5174 / 6868 already in use**
→ Change the host-side ports in `docker-compose.yml` (e.g., `"5175:5174"`), or set `ASPNETCORE_URLS` when running from source.

---

Project by [@OlivierDeNeef](https://github.com/OlivierDeNeef).
