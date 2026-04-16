---
name: stats
description: Show MemoryBank memory statistics, health status, categories, and tags overview.
user-invocable: true
argument-hint: "[health|categories|tags]"
---

# MemoryBank Stats

Overview of your second mind's current state.

## When Invoked

1. **`/stats`** — Full overview (stats + health)
2. **`/stats health`** — Health check only
3. **`/stats categories`** — Category tree with counts
4. **`/stats tags`** — Tag list with usage counts

## Process

### Step 1: Determine scope (main agent)

Identify what the user wants: full overview, health only, categories, or tags.

### Step 2: Spawn a subagent (main agent)  ! important !

Use the **Agent tool** to spawn a single subagent. Pass it a prompt with the scope and the instructions below. Do NOT call any `mcp__memorybank__*` tools yourself.

**Subagent prompt must include:**
- The requested scope (full/health/categories/tags)
- The subagent instructions from Step 3

### Step 3: Subagent instructions

> These instructions are for the subagent, include them in the Agent tool prompt.

**Full overview:** Call `mcp__memorybank__memory_stats()` and `mcp__memorybank__health_check()` in parallel.

**Health only:** Call `mcp__memorybank__health_check()`.

**Categories:** Call `mcp__memorybank__list_categories()`.

**Tags:** Call `mcp__memorybank__list_tags(sort: "count", limit: 50)`.

**Return** all results as structured text. For stats: total memories, chunks, revisions, database size, type distribution, priority distribution, top categories, top tags. For health: status and summary. For categories: tree with counts. For tags: sorted list with counts. Exclude raw JSON and internal field names.

### Step 4: Present results (main agent)

Take the subagent's response and format as a clean dashboard.

**Full overview format:**

> ### MemoryBank Overview
>
> | Metric | Value |
> |--------|-------|
> | Memories | <total> |
> | Chunks | <total> |
> | Revisions | <total> |
> | Database size | <size in human-readable format> |
>
> **Types:** <type distribution, e.g. "42 facts, 12 decisions, 8 procedures">
> **Priority:** <distribution, e.g. "3 critical, 15 high, 28 normal">
> **Top categories:** <top 5 with counts>
> **Top tags:** <top 10 with counts>
>
> **Health:** <status> <summary, e.g. "All systems healthy">
> *Last backup: <date>*

**Categories view:** Display as an indented tree with counts.

**Tags view:** Display sorted by count: **Tags:** blazor (18) · api (15) · auth (12) · ...

**Never show:** raw JSON, internal field names, MCP tool names, or technical database details unless the user asks for diagnostics.
