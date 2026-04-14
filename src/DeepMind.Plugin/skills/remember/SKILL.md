---
name: deepmind:remember
description: Store new knowledge in DeepMind. Use when the user wants to save facts, decisions, procedures, or references.
user-invocable: true
argument-hint: "<content> [--category <path>] [--tags <t1,t2>] [--type <fact|decision|procedure|reference|observation>] [--priority <1-5>]"
---

# DeepMind Remember

Store knowledge in your second mind for future recall.

## When Invoked

1. **`/deepmind:remember <content>`** — Store the provided content
2. **`/deepmind:remember` (no args)** — Prompt user for what to store, or extract from conversation context
3. **AI auto-invokes** — When user says "remember this", "save that", "store this for later"

## Process

### Step 1: Determine Content

If content provided as argument, use it directly.

If no content, analyze the current conversation for:
- Decisions made
- Facts learned
- Procedures discovered
- References shared

Ask the user to confirm what should be stored.

### Step 2: Classify the Memory

Determine or ask:
- **type** — `fact` (default), `decision`, `procedure`, `reference`, `observation`
- **category** — Suggest based on content (e.g., `projects/deepmind`, `architecture/auth`)
- **priority** — 1 (trivial) to 5 (critical), default 3
- **tags** — Extract keywords from content

### Step 3: Write a Search-Optimized Summary

**CRITICAL:** The `summary` field is the most important field for search recall. Write it as a keyword-rich, search-optimized string — NOT a prose description.

Include in the summary:
- **Project/product names** and their aliases (e.g., "spieken", "'t Spieken", "frituur website")
- **All specific values** (numbers, limits, sizes, versions, URLs)
- **Key technical terms** (frameworks, protocols, patterns)
- **Common search phrases** a user might type to find this memory

**Good summary example:**
> Spieken Lebbeke frituur website - ASP.NET Blazor .NET 10, SQL Server EF Core, 10MB image upload limit, opening hours, gallery, admin panel, IIS WebDeploy, cookie auth, CSP headers, SkiaSharp WebP

**Bad summary example:**
> Frituur 't Spieken Lebbeke website project overview

### Step 4: Check for Duplicates

Before storing, search for similar content:

```
mcp__deepmind__recall(query: "<summary of content>", limit: 3)
```

If a similar memory exists (high similarity score), ask the user:
- **Update** the existing memory? → `mcp__deepmind__update_memory`
- **Store as new** anyway? → proceed
- **Link** to existing? → store + `mcp__deepmind__link_memories`

### Step 5: Store

```
mcp__deepmind__remember(
  content: "<full content>",
  summary: "<search-optimized summary from Step 3>",
  category: "<category path>",
  tags: "tag1,tag2,tag3",
  type: "<type>",
  priority: <1-5>,
  source: "conversation"
)
```

### Step 6: Enrich Chunks (if chunked)

If the response shows `chunkCount > 1` and includes `chunkPreviews`, you MUST call `enrich_chunks` to improve per-chunk searchability.

For each chunk preview, write:
- **summary**: A 1-2 sentence description of what that specific chunk covers. Include all specific values, names, limits, and searchable terms from that chunk.
- **keywords**: Comma-separated search terms specific to that chunk's content (not just the parent tags — add chunk-specific terms).

```
mcp__deepmind__enrich_chunks(
  memoryId: "<id from step 5>",
  enrichments: "[{\"chunkIndex\": 0, \"summary\": \"...\", \"keywords\": \"...\"}, ...]"
)
```

### Step 7: Confirm

Report back:
- Memory ID
- Summary applied
- Category and tags applied
- Chunk count (if large content was split)
- Whether chunks were enriched
- Duplicate warning (if any)
