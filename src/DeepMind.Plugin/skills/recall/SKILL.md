---
name: deepmind:recall
description: Search and recall memories from DeepMind using hybrid semantic + keyword search. Use when looking up stored knowledge.
user-invocable: true
argument-hint: "<query> [--category <path>] [--tags <t1,t2>] [--type <fact|decision|procedure|reference|observation>] [--limit <n>]"
---

# DeepMind Recall

Search your second mind for stored knowledge using hybrid search (semantic + keyword + priority).

## When Invoked

1. **`/deepmind:recall <query>`** — Search for memories matching the query
2. **`/deepmind:recall` (no args)** — Show recent memories (last 24h)
3. **AI auto-invokes** — When user asks "what do we know about...", "recall...", "remember..."

## Process

### Step 1: Parse Arguments

Extract from user input:
- **query** — The search text (required unless showing recent)
- **--category** — Filter to category path (e.g., `projects/backend`)
- **--tags** — Comma-separated tag filter
- **--type** — Memory type filter: `fact`, `decision`, `procedure`, `reference`, `observation`
- **--limit** — Max results (default: 10)
- **--priority** — Minimum priority (1-5)

### Step 2: Execute Search

If query provided, use `mcp__deepmind__recall`:

```
mcp__deepmind__recall(
  query: "<search text>",
  category: "<if specified>",
  tags: "<if specified>",
  type: "<if specified>",
  limit: <number>,
  minPriority: <if specified>
)
```

If no query, use `mcp__deepmind__recall_recent`:

```
mcp__deepmind__recall_recent(hoursBack: 24)
```

### Step 3: Present Results

For each result, display:
- **Summary** or first line of content
- **Category** and **tags**
- **Type** and **priority**
- **Score** (if from search)
- **Created/updated** dates

If a result is chunked, mention it and offer to fetch full content with `mcp__deepmind__get_memory` or `mcp__deepmind__get_chunks`.

### Step 4: Follow-up

If results seem insufficient, suggest:
- Broadening the query
- Trying different category/tag filters
- Checking archived memories with `includeArchived: true`
