---
name: remember
description: Store new knowledge in MemoryBank. Use when the user wants to save facts, decisions, procedures, or references.
user-invocable: true
argument-hint: "<content> [--category <path>] [--tags <t1,t2>] [--type <fact|decision|procedure|reference|observation>] [--priority <1-5>]"

---

# MemoryBank Remember

Store knowledge in your second mind for future recall.

## When Invoked

1. **`/remember <content>`** — Store the provided content
2. **`/remember` (no args)** — Prompt user for what to store, or extract from conversation context
3. **AI auto-invokes** — When user says "remember this", "save that", "store this for later"

## Process

### Step 1: Determine Content (main agent)

If content provided as argument, use it directly.

If no content, analyze the current conversation for:
- Decisions made
- Facts learned
- Procedures discovered
- References shared

Ask the user to confirm what should be stored.

### Step 2: Classify the Memory (main agent)

Determine or ask:
- **type** — `fact` (default), `decision`, `procedure`, `reference`, `observation`
- **category** — Suggest based on content (e.g., `projects/memorybank`, `architecture/auth`)
- **priority** — 1 (trivial) to 5 (critical), default 3
- **tags** — Extract keywords from content

### Step 3: Write a Search-Optimized Summary (main agent)

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

### Step 4: Spawn a subagent (main agent)  ! important !

Use the **Agent tool** to spawn a single subagent. Pass it a prompt containing the content, summary, classification, and the instructions below. Do NOT call any `mcp__memorybank__*` tools yourself.

**Subagent prompt must include:**
- The full content to store
- The search-optimized summary from Step 3
- The classification (type, category, priority, tags)
- The subagent instructions from Step 5

### Step 5: Subagent instructions

> These instructions are for the subagent, include them in the Agent tool prompt.

**Check for duplicates:** Call `mcp__memorybank__recall(query: "<summary>", limit: 3)`. If a very similar memory exists (high similarity score), return that information so the main agent can ask the user whether to update or store as new.

**Store the memory:** Call `mcp__memorybank__remember` with all the fields: content, summary, category, tags, type, priority, source: "conversation".

**Enrich chunks:** If the response shows `chunkCount > 1` and includes `chunkPreviews`, call `mcp__memorybank__enrich_chunks`. For each chunk, write a summary (1-2 sentences with specific values and searchable terms) and keywords (comma-separated, chunk-specific).

**Return:** The stored memory's title/summary, category, tags, type, priority, chunk count, revision number, date, and whether a duplicate was detected (with the duplicate's title if so). Exclude: memory UUID, raw chunk previews, embedding details, raw JSON.

### Step 6: Present result (main agent)

Take the subagent's response and present it cleanly:

> **Memory saved**
>
> **Title:** <summary, shortened to a readable title>
> **Category:** <category>
> **Tags:** <tag1>, <tag2>, <tag3>
> **Type:** <type> · **Priority:** <priority as label: Trivial/Low/Normal/High/Critical>
>
> <content preview — first ~3 lines or a concise summary>
>
> *<chunk count> chunks · Revision 1 · Saved <date>*

If a duplicate was detected, mention it briefly: *"Similar to existing memory: <title>"*

Do NOT show: memory UUID, raw chunk previews, embedding details, or MCP tool call names.
