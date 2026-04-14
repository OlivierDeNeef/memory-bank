---
name: deepmind:stats
description: Show DeepMind memory statistics, health status, categories, and tags overview.
user-invocable: true
argument-hint: "[health|categories|tags]"
---

# DeepMind Stats

Overview of your second mind's current state.

## When Invoked

1. **`/deepmind:stats`** — Full overview (stats + health)
2. **`/deepmind:stats health`** — Health check only
3. **`/deepmind:stats categories`** — Category tree with counts
4. **`/deepmind:stats tags`** — Tag list with usage counts

## Process

### Default: Full Overview

Run in parallel:

```
mcp__deepmind__memory_stats()
mcp__deepmind__health_check()
```

Present:
- Total memories, chunks, revisions
- Database file size
- Memory type distribution
- Priority distribution
- Category count and top categories
- Tag count and top tags
- Health status (DB integrity, FTS, embedding model)
- Last backup date

### Categories View

```
mcp__deepmind__list_categories()
```

Display as indented tree with memory counts.

### Tags View

```
mcp__deepmind__list_tags(sort: "count", limit: 50)
```

Display sorted by usage count.
