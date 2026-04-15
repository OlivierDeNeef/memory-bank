---
name: search
description: Advanced memory search with filters, sorting, and exploration. Use for complex queries beyond simple recall.
user-invocable: true
argument-hint: "<query> [--category <path>] [--tags <t1,t2>] [--type <type>] [--from <date>] [--to <date>] [--sort <relevance|date|priority>] [--archived]"
hooks:
  PreToolUse:
    - matcher: "mcp__deepmind__*"
      hooks:
        - type: command
          command: "bash ${CLAUDE_PLUGIN_ROOT}/scripts/block-direct-mcp.sh"
---

# DeepMind Search

Advanced search with full filter control. Use `/deepmind:recall` for quick lookups; use this for complex queries.

## When Invoked

1. **`/search <query> [filters]`** — Advanced filtered search
2. **`/search --archived`** — Search including archived memories

## Process

### Step 1: Parse Filters (main agent)

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

### Step 2: Spawn a subagent (main agent)  ! important !

Use the **Agent tool** to spawn a single subagent. Pass it a prompt containing the parsed filters and the instructions below. Do NOT call any `mcp__deepmind__*` tools yourself.

**Subagent prompt must include:**
- All parsed filters from Step 1
- The subagent instructions from Step 3

### Step 3: Subagent instructions

> These instructions are for the subagent, include them in the Agent tool prompt.

**Search:** Call `mcp__deepmind__recall` with the query and all filters (category, tags, tagMode, type, dateFrom, dateTo, minPriority, sort, limit, offset, includeArchived).

**Fetch chunks:** For any result with `chunkCount > 1`, call `mcp__deepmind__get_memory` or `mcp__deepmind__get_chunks` to get the full content. Do this automatically.

**Return the results** as structured text. For each result include: summary/title, category, tags, type, priority, similarity score as percentage, revision number, last updated date, and full reconstructed content. Also return the total count and whether `hasMore` is true. Exclude: memory UUIDs, chunk indices, overlap metadata, embedding vectors, raw JSON, score breakdowns.

### Step 4: Present results (main agent)

Take the subagent's response and present cleanly.

**Search summary line at the top:**

> Found **<count>** memories matching "<query>" <filters applied>

**For each result:**

> **<n>. <Title/Summary>**
> <category> · <tags> · **Confidence:** <score as %>
> **Type:** <type> · **Priority:** <priority label>
> *Revision <n> · Last updated <date>*
>
> <Content preview or full content if short>

If `hasMore` is true, mention: *"More results available — ask to see the next page."*

**Never show:** memory UUIDs, chunk indices, overlap metadata, raw similarity scores (convert to %), embedding vectors, MCP tool names, JSON structures, score breakdowns.

### Step 5: Actions

After presenting results, offer relevant follow-up actions in natural language:
- "Want me to update, delete, pin, or archive any of these?"
- "Want to see the full content of any result?"
- "Want me to link related memories together?"

Do NOT show raw MCP tool names or commands in suggestions.
