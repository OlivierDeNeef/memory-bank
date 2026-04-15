# DeepMind.Web

3D force-graph viewer for the DeepMind memory store. Read-only.

## Stack

- **Backend**: ASP.NET Core (.NET 10) minimal API, references `DeepMind.Core`
- **Frontend**: Vite + React + TypeScript + Tailwind + `react-force-graph-3d` + three.js

## Running in development

Two terminals:

```bash
# Terminal 1 — API on :5174
dotnet run --project src/DeepMind.Web

# Terminal 2 — Vite dev server on :5173 (proxies /api to :5174)
cd src/DeepMind.Web/ClientApp
npm install
npm run dev
```

Open http://localhost:5173.

## Production build

```bash
dotnet publish src/DeepMind.Web -c Release
```

The MSBuild pipeline runs `npm ci && npm run build` and ships the static bundle in `wwwroot`. A single `dotnet DeepMind.Web.dll` serves both the API and the SPA on `http://localhost:5174`.

## API

- `GET /api/filters` — sidebar options (categories, tags, types)
- `GET /api/graph` — main graph payload
  - `edgeTypes=links,similarity,tags,category` (multi-select, at least one required)
  - `categories`, `tags`, `types` (csv filters)
  - `simThreshold` (default 0.78), `simTopK` (default 5)
  - `tagJaccardMin` (default 0.3)
  - `includeArchived` (default false)
  - `limit` (max 1000)
- `GET /api/memory/{id}` — detail panel payload
- `GET /api/search?q=...` — hybrid search, returns `[{ id, score }]` for highlighting

## Edge types

| Type | Source |
|---|---|
| `links` | Explicit rows in `memory_links` |
| `similarity` | Cosine over mean-pooled chunk embeddings (top-K per node) |
| `tags` | Jaccard overlap of tag sets above threshold |
| `category` | Memories sharing a `category_id` |

When more than `limit` memories match filters, selection priority is: pinned → highest `access_count` → most recent.
