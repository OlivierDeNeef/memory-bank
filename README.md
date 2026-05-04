# MemoryBank

A persistent, searchable memory system for AI assistants, exposed as an MCP server — plus a 2D force-directed graph viewer for exploring what you've stored.

- **Store**: todos, decisions, references, guides — tagged, categorized, versioned
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

**Auth is required for HTTP mode.** The MCP server refuses to start without `MEMORYBANK_AUTH_USERNAME` and `MEMORYBANK_AUTH_PASSWORD_HASH`. Generate the hash, then create a `.env`:

```bash
# Generate a password hash (writes a single line to stdout)
dotnet run --project src/MemoryBank.Server -- --hash-password 'pick-a-strong-password'

# Copy the template and paste the hash you just generated
cp .env.example .env
$EDITOR .env
```

Then start the stack:

```bash
docker compose up -d
```

Then open:
- Graph viewer: <http://localhost:5174> (browser will prompt you to sign in)
- MCP server health: <http://localhost:6868/health> (the only endpoint that bypasses auth)

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

## Hosting on a remote server (nginx + shared OAuth)

Run MemoryBank on a server reachable from all your devices, with one login that covers both the MCP server and the graph viewer.

The architecture collapses both services onto a single hostname so cookies and OAuth callbacks share an origin:

```
https://memory-bank.example.com/
├── /                       → MemoryBank.Web    (graph viewer SPA + API)
├── /api/*                  → MemoryBank.Web
├── /auth/*                 → MemoryBank.Web    (login redirect, callback, logout)
├── /mcp                    → MemoryBank.Server (MCP protocol)
├── /oauth/*                → MemoryBank.Server (login form, token endpoint)
└── /.well-known/*          → MemoryBank.Server (OAuth discovery metadata)
```

`MemoryBank.Server` is the **authorization server** (renders the login page, issues tokens). `MemoryBank.Web` is a **resource server** (validates tokens against the shared SQLite DB). Both processes run side-by-side on the Docker host; nginx (or Nginx Proxy Manager) does TLS termination and path routing.

### 1. On the server: bring up the stack

```bash
# Generate a password hash on the server (or any machine with the .NET SDK)
dotnet run --project src/MemoryBank.Server -- --hash-password 'pick-a-strong-password'

# Create .env with the username + hash you just produced
cp .env.example .env
$EDITOR .env

# Start both containers
docker compose up -d
```

The MCP server binds `0.0.0.0:6868` and the viewer binds `0.0.0.0:5174` on the host. Make sure your **server firewall blocks those ports from the public internet** — only the local nginx should reach them.

### 2. Configure the proxy host in Nginx Proxy Manager

Add a new proxy host:

**Details tab**
- Domain Name: `memory-bank.example.com`
- Forward Hostname/Port: `<host-LAN-IP>` `:5174` (the viewer is the default upstream)
- Block Common Exploits: on
- Websockets Support: on (the MCP transport keeps the connection open with SSE-style framing)

**Custom Locations** — three entries that route the auth-server paths to the MCP container:

| Location | Forward Hostname (LAN IP) | Port |
|---|---|---|
| `/mcp` | `<host-LAN-IP>` | `6868` |
| `/oauth` | `<host-LAN-IP>` | `6868` |
| `/.well-known` | `<host-LAN-IP>` | `6868` |

Open the **Advanced** subsection of the `/mcp` location and paste:

```nginx
proxy_buffering off;
proxy_cache off;
proxy_read_timeout 1h;
chunked_transfer_encoding on;
```

(MCP HTTP transport keeps long-lived streaming responses open. Without these, NPM's default 60s read timeout will cut connections mid-call.)

**SSL tab**: request a Let's Encrypt cert, force SSL, enable HTTP/2.

Save. The browser flow is now: visit `https://memory-bank.example.com/` → 302 to `/auth/login` → 302 to `/oauth/authorize` (login form) → submit → 302 back to `/auth/callback` → 302 to `/`. The graph loads.

### 3. Register the MCP on each device

Per device, one command — Claude Code handles the OAuth dance via your browser:

```bash
claude mcp add --transport http --scope user memorybank https://memory-bank.example.com/mcp
```

The first call to a MemoryBank tool will pop a browser to the login page. After you sign in, Claude Code caches the bearer + refresh tokens locally; subsequent sessions are silent.

To revoke a device:

```bash
# Removes the local token cache; the refresh token in SQLite stays until it expires.
# To kill it server-side too, log in to the viewer and POST /auth/logout, or wipe the
# row directly from oauth_refresh_tokens.
claude mcp remove memorybank
```

### Rolling back

If something goes wrong:

```bash
docker compose down            # stop containers (data persists in the volume)
docker compose down -v         # nuke the volume too — wipes the database AND all OAuth tokens
git revert <commit>            # roll back to a previous deployment
```

The schema migration v4 only **adds** OAuth tables; rolling back to a pre-auth version still works against the same DB (the tables just go unused).

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

## Architecture

A detailed walk-through of how DeepMind is put together. Section 1 covers the shared library, section 2 the MCP server, section 3 the web viewer, section 4 the Claude Code plugin, and section 5 the end-to-end flows that tie it all together.

### 1. DeepMind.Core — storage, search, embeddings

#### SQLite schema (`Storage/Migrations.cs`)

Schema v3; every table has `created_at`, foreign keys are `ON DELETE CASCADE`. The migration runner toggles `PRAGMA foreign_keys` OFF around migrations so CHECK/column rebuilds (like v3) don't cascade-delete dependent rows; a `foreign_key_check` sanity-check runs after all pending migrations.

| Table | Purpose |
|---|---|
| `memories` | core record: `id`, `content`, `summary`, `category_id`, `type` (todo\|decision\|reference\|guide — schema v3; see `Storage/Migrations.cs`), `priority` 1–5, `is_pinned`, `is_archived`, `access_count`, `revision_number`, `token_count`, `valid_from/until`, `source`, `metadata` (JSON), timestamps |
| `categories` | hierarchical (self-FK via `parent_id`), unlimited depth |
| `revisions` | immutable history; UNIQUE(memory_id, revision_number) |
| `chunks` | split content per memory for embedding/FTS; UNIQUE(memory_id, chunk_index) |
| `embeddings` | BLOB vectors keyed by `chunk_id` + `model` name |
| `tags` + `memory_tags` | normalized many-to-many |
| `memory_links` | explicit edges (source_id, target_id, link_type) — `Related` / `Supersedes` / `Contradicts` / `Extends` |
| `audit_log` | every create/update/delete/recall |
| `schema_version` | tracks applied migrations |

Plus an **FTS5 virtual table** `chunks_fts` over `(content, summary, keywords)` with `porter unicode61` tokenizer. Three triggers (`chunks_ai/ad/au`) keep FTS in sync. BM25 column weights at query time: `1.0 / 2.0 / 3.0` (keywords weighted highest). Indexes on category, priority, type, created, archived (memories); memory_id/chunk_id (embeddings); tag name; category parent.

#### Connection (`Storage/DeepMindDb.cs`)

On open, applies `PRAGMA journal_mode=WAL`, `busy_timeout=5000`, `foreign_keys=ON`. Exposes `CreateCommand`, `BeginTransaction`, `Checkpoint()` (WAL truncate), `Vacuum()`.

#### MemoryStore (`Storage/MemoryStore.cs`)

All mutating operations wrap `BeginTransaction` + rollback on throw. Highlights:

- `Insert`: memory → tag links → chunks → audit, all in one transaction.
- `Update`: snapshots current row into `revisions` before applying a **dynamic UPDATE** (only non-null fields). Bumps `revision_number`.
- `Delete` / `BulkDelete`: cascades via FKs, then cleans orphan tags and orphan categories iteratively.
- `ListMemories`: filters by category-IDs, tags (AND/OR mode), types, archived; orders `pinned DESC, access_count DESC, created_at DESC`; returns `(rows, totalCount)`.
- `EnsureCategoryPath("a/b/c")`: creates nested hierarchy on demand, returns leaf id.
- Graph helpers: `GetLinksWithin(ids)`, `GetTagAssignmentsFor(ids)`, `GetAllEmbeddings()` for the viewer.
- `IncrementAccessCount` on every recall hit; `last_accessed` updated.

#### BackupService (`Storage/BackupService.cs`)

Uses `SqliteConnection.BackupDatabase()` for atomic snapshots, files named by timestamp. `IsBackupDue` checks interval (default 24 h), retention keeps the last `MaxBackups` (default 10). Restore runs `PRAGMA integrity_check` on the backup file before copy.

#### Hybrid search (`Search/HybridSearchEngine.cs`)

Final score formula:

```
finalScore =
    VectorWeight   * vectorScore     // default 0.40
  + KeywordWeight  * keywordScore    // default 0.35
  + PriorityWeight * priorityScore   // default 0.25
  + pinBonus (if pinned, +0.5)
  + log2(accessCount + 1) * AccessBoostFactor / 100
  - recencyDays * RecencyDecayPerDay / 100
```

- **Keyword phase**: sanitizes the query (strips quotes, supports `NOT`), runs `bm25(chunks_fts, 1.0, 2.0, 3.0)` — keywords column weighted highest — deduplicates to best score per memory, then normalizes to 0..1 by dividing by max.
- **Vector phase**: embeds the query with `search_query:` prefix, cosine vs. every stored embedding, minimum similarity 0.1, best per memory.
- **Filters**: category (expanded to descendant IDs), tags (AND/OR), types, priority floor, date window, validity (`valid_from/until`), archived gate.
- `FindSimilar` reuses the vector path for duplicate detection (default threshold 0.90) during `remember`.
- `SearchRecent` short-circuits text search when only a date window is given.
- Every hit writes an audit row (`recalled`).

#### Chunking (`Search/ChunkingService.cs`)

Token count ≈ `wordCount * 1.33`. If text fits in `MaxTokensPerChunk` (default 400), one chunk. Otherwise: split by sentence (regex on `.!?\n`), greedily pack up to the budget, and **keep the last few sentences as overlap** (default 50 tokens) into the next chunk for continuity. Each chunk's `summary` gets prefixed with memory-level context: `[summary | category/path | tag1, tag2] …`. Keywords propagate from the memory's tags.

#### ONNX embeddings (`Embeddings/OnnxEmbeddingService.cs`)

Loads `nomic-embed-text-v1.5` (768-d) via `InferenceSession`. BertTokenizer from `vocab.txt` (falls back to hash-based tokens if missing). At inference:

1. Prefix text — `search_query:` for queries, `search_document:` for stored content (Nomic's task-conditioned prefixes).
2. Tokenize, cap at 512, build `input_ids` / `attention_mask` / optional `token_type_ids` tensors.
3. Mean-pool across attended tokens (mask-weighted average).
4. L2-normalize.

If the model file is missing, vector search silently falls back — keyword search keeps working, similarity edges are empty in the viewer.

#### Configuration (`Configuration/DeepMindConfiguration.cs`)

Sections: `Database` (path, WAL, busy timeout), `Backup` (path, interval, max), `Embedding` (path, name, dims, chunk size/overlap), `Search` (weights, pin bonus, decay, access boost, limits), `Validation` (max content 100 k, max tags 50, max category depth 10, etc.), `MemoryDefaults` (priority 3, type reference, duplicate threshold 0.90), `Logging` (path, rotation). All overridable via `appsettings.json` or `DeepMind__*` env vars.

### 2. DeepMind.Server — MCP surface

#### Startup (`Program.cs`)

Transport picked via `--http` flag or `DEEPMIND_TRANSPORT=http`. DI registers `DeepMindDb`, `MemoryStore`, `OnnxEmbeddingService`, `ChunkingService`, `HybridSearchEngine`, `BackupService` as singletons. Either `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()` (stdio, logs to stderr so stdout is clean protocol) or `.WithHttpTransport()` + `MapMcp("/mcp")` + `MapOAuth("/mcp")` + `/health`. On startup, `RunAutoBackup` fires if `IsBackupDue`.

#### OAuth (`OAuthEndpoints.cs`)

PKCE OAuth 2.1 for the MCP Streamable-HTTP auth handshake:

- `.well-known/oauth-protected-resource/mcp` + `.well-known/oauth-authorization-server` metadata
- `POST /oauth/register` — auto-issues `client_id`
- `GET /oauth/authorize` — issues code with PKCE challenge, redirects
- `POST /oauth/token` — verifies S256 challenge, 5-min code window, 24-h token

#### Tools (`Tools/*.cs`)

Each tool uses `[McpServerTool]` and is exposed as `mcp__deepmind__<name>`:

| File | Tools |
|---|---|
| `RememberTool` | `remember` — validate → `FindSimilar` dupe check → chunk with context → transactional insert |
| `RecallTool` | `recall`, `recall_recent`, `exists` |
| `ManageTool` | `get_memory`, `get_chunks`, `pin`/`unpin`, `archive`/`unarchive`, `update_memory`, `forget`, `link_memories`, `unlink_memories`, `get_linked` |
| `CategoryTool` | `list_categories`, `create_category`, `rename_category`, `delete_category`, `move_memory`, `list_tags`, `rename_tag`, `merge_tags` |
| `BulkTool` | `bulk_tag`, `bulk_recategorize`, `bulk_priority`, `bulk_forget`, `cleanup_orphans` |
| `RevisionTool` | `get_revisions`, `get_revision`, `diff_revisions`, `restore_revision` |
| `HealthTool` | `health_check`, `memory_stats`, `count`, `backup`, `restore_backup` |
| `ExportImportTool` | `export`, `import` (JSON) |
| `EnrichChunksTool` | `enrich_chunks`, `reembed_all` |
| `ContextTool` | `recall_context` — semantic pull for a broad topic |

### 3. DeepMind.Web — graph viewer

#### Backend (`Program.cs`, `Endpoints/GraphEndpoints.cs`, `Services/GraphService.cs`)

Same DI as the MCP server + `GraphService`. CORS allows `localhost:5173` for Vite dev. Pipeline: `UseDefaultFiles` → `UseStaticFiles` → `MapGraphEndpoints` → SPA fallback to `index.html`.

Endpoints:

- `GET /api/filters` — categories, tags, types, counts, embedding availability
- `GET /api/graph?edgeTypes=…&categories=…&tags=…&types=…&includeArchived=…&simThreshold=0.78&simTopK=5&tagJaccardMin=0.3&limit=…`
- `GET /api/memory/{id}` — content, linked memories, revision history, chunks
- `GET /api/search?q=…` — live hybrid search for node scoring

`GraphService.Build`:

1. Resolve category paths → IDs (+ descendants), call `ListMemories`.
2. Build nodes (`id`, `label` = summary or first 80 chars, `type`, `categoryId/Path`, `tags`, `priority`, `pinned`, `accessCount`, `createdAt`).
3. Build edges per opted-in type:
   - **Links** — `GetLinksWithin(nodeIds)`, weight 1.0.
   - **Tags** — pairwise **Jaccard** on tag sets, kept if ≥ `tagJaccardMin`.
   - **Category** — all pairs sharing a category, weight 1.0.
   - **Similarity** — mean-pool each memory's chunk embeddings, cosine over all pairs, keep top-K per node above `simThreshold`, canonicalized to dedupe.

#### Frontend (`ClientApp/`)

`App.tsx` orchestrates: FilterPanel (left), Graph2D/Graph3D (center, toggled), DetailPanel (right), Legend. Filter changes → **200 ms debounce** → fetch `/api/graph` with an `AbortController` to cancel stale requests. `focusId = hoveredNodeId ?? selectedNodeId`.

`store.ts` (Zustand) holds filter state, `viewMode` (`2d`/`3d`), `selectedNodeId`, `hoveredNodeId`, `searchQuery`, and a `searchScores: Map<id, 0..1>` used to scale node sizes during search.

`Graph2D.tsx` wires `react-force-graph-2d` with custom d3 forces:

- `charge`: `-160 * (nodeSize / baseRadius)`, `distanceMax=360`
- `link`: `distance = 200 - strength*140`, `strength = 0.4 + strength*0.5`
- `collide`: `nodeSize + 6`, strength `0.9`
- Node positions cached across filter refreshes so the graph smoothly morphs instead of re-laying-out.
- Search reheats the simulation when `searchScores` change.

`Graph3D.tsx` uses `react-force-graph-3d` + Three.js with `three-spritetext` labels.

`FilterPanel.tsx` exposes edge-type checkboxes, multi-select for categories/tags/types, threshold sliders (`simThreshold`, `tagJaccardMin`), and a 120 ms debounced live-search box that populates `searchScores`.

Key deps: `react-force-graph-2d` 1.27, `react-force-graph-3d` 1.26, `three` 0.171, `three-spritetext` 1.9, `d3-force` 3, `zustand` 5, `lucide-react`.

### 4. DeepMind.Plugin — Claude Code integration

`.claude-plugin/plugin.json` declares the plugin, skills directory, and two `PreToolUse` hooks:

- `scripts/block-direct-mcp.sh` — intercepts raw `mcp__deepmind__*` calls and redirects the agent to the skill layer, so tool noise stays out of the main context.
- `scripts/block-auto-memory.sh` — prevents Claude's auto-memory from accidentally writing Read/Write/Edit scaffolding into DeepMind.

Skills under `skills/{remember,recall,search,stats,todo,forget}/SKILL.md` each document purpose, parameters, and output shape. `CLAUDE.md` mandates that skill execution runs inside a single subagent via the Agent tool so only the clean final result surfaces.

### 5. End-to-end flows

**Store (`/remember`)**: skill → subagent → `mcp__deepmind__remember` → validate → `FindSimilar` (≥0.90 → duplicate warning) → `ChunkingService.ChunkText` (sentence-pack + overlap, add context prefix) → embed each chunk (`search_document:`) → transactional insert of memory + tag links + chunks + embeddings + audit → return `{id, chunkCount, elapsedMs}`.

**Recall (`/recall`)**: skill → subagent → `mcp__deepmind__recall` → parse filters → `HybridSearchEngine.Search` → BM25(FTS5) ∪ cosine(ONNX) → merge + priority-weight + pin bonus + access boost − recency decay → sort/paginate → audit → ranked results.

**Update**: `update_memory` snapshots the current row into `revisions`, dynamically updates non-null fields, re-chunks and re-embeds if content changed, bumps `revision_number`. `restore_revision` pulls a past snapshot back into the live row (and itself creates a new revision).

**Graph explore**: Vite SPA → `/api/graph` → `GraphService.Build` assembles filtered nodes and the requested edge types (links / tag-Jaccard ≥ 0.3 / same category / cosine top-K ≥ 0.78) → react-force-graph renders; clicking a node pulls `/api/memory/{id}` into the detail panel (content, revisions, linked memories, chunks); search rescales nodes by hybrid score.

**Backup**: auto-checked on server startup; if interval elapsed, `SqliteConnection.BackupDatabase()` snapshots to `~/.deepmind/backups/`, retention trims the oldest.

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

**Server exits at startup with "HTTP transport requires MEMORYBANK_AUTH_USERNAME and MEMORYBANK_AUTH_PASSWORD_HASH"**
→ Auth env vars are missing. Generate a hash with `dotnet run --project src/MemoryBank.Server -- --hash-password '<pw>'` and put `MEMORYBANK_AUTH_USERNAME=...` and `MEMORYBANK_AUTH_PASSWORD_HASH=...` into `.env` (or the container's environment).

**OAuth login redirects loop or the metadata advertises `http://` instead of `https://`**
→ nginx isn't forwarding the right headers. The proxy host needs `proxy_set_header X-Forwarded-Proto $scheme;` (NPM enables this by default). Without it, `MemoryBank.Server` builds the auth URLs from the request's plain-HTTP scheme, so cookies miss `Secure` and the browser refuses to send them back.

**`Set-Cookie` arrives but the next request has no cookie**
→ You're loading the viewer over HTTP while the cookie is `Secure`. Use HTTPS (the production setup), or unset `IsHttps` in dev. The cookie code reads `ctx.Request.IsHttps` — make sure `UseForwardedHeaders` runs before the cookie is set.

**`/mcp` returns 401 even after a successful login in the browser**
→ The MCP transport doesn't carry the browser's cookies. Use `claude mcp add --transport http memorybank https://memory-bank.example.com/mcp` so Claude Code drives its own OAuth flow and stores its own bearer token.

---

Project by [@OlivierDeNeef](https://github.com/OlivierDeNeef).
