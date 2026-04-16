---
name: recall
description: Search and recall memories from MemoryBank using hybrid semantic + keyword search. Use when looking up stored knowledge.
user-invocable: true
argument-hint: "<query> [--category <path>] [--tags <t1,t2>] [--type <fact|decision|procedure|reference|observation>] [--limit <n>]"
---

# MemoryBank Recall

Search your second mind for stored knowledge using hybrid search (semantic + keyword + priority).

## When Invoked

1. **`/recall <query>`** — Search for memories matching the query
2. **`/recall` (no args)** — Show recent memories (last 24h)
3. **AI auto-invokes** — When user asks "what do we know about...", "recall...", "remember..."

## Process

### Step 1: Parse Arguments (main agent)

Extract from user input:
- **query** — The search text (required unless showing recent)
- **--category** — Filter to category path (e.g., `projects/backend`)
- **--tags** — Comma-separated tag filter
- **--type** — Memory type filter: `fact`, `decision`, `procedure`, `reference`, `observation`
- **--limit** — Max results (default: 10)
- **--priority** — Minimum priority (1-5)

### Step 2: Spawn a subagent (main agent) ! important !

Use the **Agent tool** to spawn a single subagent. Pass it a prompt containing the parsed arguments and the instructions below. Do NOT call any `mcp__memorybank__*` tools yourself.

**Subagent prompt must include:**
- The parsed query and all filters from Step 1
- The subagent instructions from Step 3

### Step 3: Subagent instructions

> These instructions are for the subagent, include them in the Agent tool prompt.

**Search:** If query was provided, call `mcp__memorybank__recall` with the query and any filters. If no query, call `mcp__memorybank__recall_recent(hoursBack: 24)`.

**Fetch chunks:** If any result has `chunkCount > 1`, call `mcp__memorybank__get_memory` or `mcp__memorybank__get_chunks` to get the full content. Do this automatically without asking.

**Return the results** as structured text the main agent can present. For each result include: summary/title, category, tags, type, priority, similarity score as percentage, revision number, last updated date, and the full reconstructed content (merged from all chunks seamlessly). Exclude: memory UUIDs, chunk indices, chunk overlap metadata, embedding vectors, raw JSON.

### Step 4: Present results (main agent)

Take the subagent's response and present it cleanly to the user. Do NOT echo raw subagent output — format it.

**For a single result:**

> **<Title/Summary>**
> **Category:** <category> · **Tags:** <tag1>, <tag2>
> **Type:** <type> · **Priority:** <priority label>
> **Confidence:** <score as %>
>
> <Full content — seamlessly merged, no chunk boundaries>
>
> *Revision <n> · Last updated <date>*

**For multiple results:**

> 1. **<Title>** — <first line or summary preview>
>    <category> · <tags> · <confidence %>
>    *Revision <n> · <date>*
>
> 2. **<Title>** — <first line or summary preview>
>    <category> · <tags> · <confidence %>
>    *Revision <n> · <date>*

**Never show:** memory UUIDs, chunk indices, chunk overlap, raw similarity scores (convert to %), embedding vectors, MCP tool names, JSON structures.

### Step 5: Follow-up

If results seem insufficient, suggest:
- Broadening the query
- Trying different category/tag filters
- Checking archived memories with `includeArchived: true`
