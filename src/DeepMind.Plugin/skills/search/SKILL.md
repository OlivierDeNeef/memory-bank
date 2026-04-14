---
name: deepmind:search
description: Advanced memory search with filters, sorting, and exploration. Use for complex queries beyond simple recall.
user-invocable: true
argument-hint: "<query> [--category <path>] [--tags <t1,t2>] [--type <type>] [--from <date>] [--to <date>] [--sort <relevance|date|priority>] [--archived]"
---

# DeepMind Search

Advanced search with full filter control. Use `/deepmind:recall` for quick lookups; use this for complex queries.

## When Invoked

1. **`/deepmind:search <query> [filters]`** — Advanced filtered search
2. **`/deepmind:search --archived`** — Search including archived memories

## Process

### Step 1: Parse Filters

Extract from user input:
- **query** — Search text (required)
- **--category** — Category path filter
- **--tags** — Comma-separated tags
- **--tag-mode** — `and` or `or` (default: `or`)
- **--type** — `fact`, `decision`, `procedure`, `reference`, `observation`
- **--from** — Date range start (ISO 8601)
- **--to** — Date range end (ISO 8601)
- **--priority** — Minimum priority (1-5)
- **--sort** — `relevance` (default), `date`, `priority`, `access_count`, `revision_count`
- **--limit** — Max results (default: 10, max: 100)
- **--offset** — Pagination offset
- **--archived** — Include archived memories

### Step 2: Execute

```
mcp__deepmind__recall(
  query: "<query>",
  category: "<if set>",
  tags: "<if set>",
  tagMode: "<if set>",
  type: "<if set>",
  dateFrom: "<if set>",
  dateTo: "<if set>",
  minPriority: <if set>,
  sort: "<if set>",
  limit: <number>,
  offset: <number>,
  includeArchived: <boolean>
)
```

### Step 3: Present Results

For each result show:
- Summary and content preview
- Score breakdown (vector, keyword, priority)
- Category, tags, type, priority
- Freshness indicator
- Chunked status

### Step 4: Navigation

If `hasMore` is true, offer to fetch next page.

If a chunked result is found, offer:
- `mcp__deepmind__get_memory` for full content
- `mcp__deepmind__get_chunks` for specific chunks

### Step 5: Actions

After presenting results, offer:
- **Update** a memory → `/deepmind:remember` with update
- **Delete** a memory → `/deepmind:forget <id>`
- **Pin/archive** → `mcp__deepmind__pin` / `mcp__deepmind__archive`
- **Link** memories → `mcp__deepmind__link_memories`
