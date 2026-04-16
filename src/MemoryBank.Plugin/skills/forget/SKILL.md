---
name: forget
description: Delete memories from MemoryBank by ID or search. Use when the user wants to remove stored knowledge.
user-invocable: true
argument-hint: "<id or search query> [--bulk] [--category <path>] [--tag <tag>]"
---

# MemoryBank Forget

Remove memories from your second mind.

## When Invoked

1. **`/forget <id>`** — Delete a specific memory by ID
2. **`/forget <query>`** — Search, show matches, confirm which to delete
3. **`/forget --bulk --category <path>`** — Bulk delete by category
4. **`/forget --bulk --tag <tag>`** — Bulk delete by tag

## Process

### Step 1: Spawn a search subagent (main agent)  ! important !

Use the **Agent tool** to spawn a subagent that finds the deletion target(s). Do NOT call any `mcp__memorybank__*` tools yourself.

**Subagent prompt:** Search for memories matching the user's query or ID. If a UUID was provided, call `mcp__memorybank__get_memory(id: "<id>")`. If a search query, call `mcp__memorybank__recall(query: "<query>", limit: 10)`. Return for each match: the memory ID, title/summary, category, tags, priority label, revision number, and created date. Exclude raw JSON, chunk metadata, embedding details.

### Step 2: Confirm deletion (main agent)

Present the candidates cleanly and ask for confirmation:

> **About to delete:**
>
> **<Title/Summary>**
> **Category:** <category> · **Tags:** <tags>
> **Priority:** <priority label>
> *Revision <n> · Created <date>*
>
> Are you sure?

For bulk operations:

> **About to delete <count> memories:**
> Filter: <category or tag being applied>
>
> Are you sure?

Do NOT show raw IDs, chunk counts, or internal metadata in the confirmation.

### Step 3: Spawn a deletion subagent (main agent)

After user confirms, use the **Agent tool** to spawn a subagent that performs the deletion. Do NOT call any `mcp__memorybank__*` tools yourself.

**Subagent prompt:** Delete the confirmed memory/memories. For single delete: call `mcp__memorybank__forget(id: "<memory-id>")`. For bulk delete: call `mcp__memorybank__bulk_forget(category: "<if set>", tag: "<if set>")`. Return the count of deleted memories and their titles.

### Step 4: Report (main agent)

Confirm what was deleted concisely:

> **Deleted:** <title/summary>
> *<n> memories removed*

Do NOT show raw deletion details like chunk counts, revision counts, embedding counts, or internal IDs.
