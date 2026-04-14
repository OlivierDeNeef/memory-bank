---
name: deepmind:forget
description: Delete memories from DeepMind by ID or search. Use when the user wants to remove stored knowledge.
user-invocable: true
argument-hint: "<id or search query> [--bulk] [--category <path>] [--tag <tag>]"
---

# DeepMind Forget

Remove memories from your second mind.

## When Invoked

1. **`/deepmind:forget <id>`** — Delete a specific memory by ID
2. **`/deepmind:forget <query>`** — Search, show matches, confirm which to delete
3. **`/deepmind:forget --bulk --category <path>`** — Bulk delete by category
4. **`/deepmind:forget --bulk --tag <tag>`** — Bulk delete by tag

## Process

### Step 1: Identify Target

**If UUID provided** — target is clear, proceed to confirmation.

**If search query provided** — find matching memories:

```
mcp__deepmind__recall(query: "<query>", limit: 10)
```

Present results and ask the user which to delete.

### Step 2: Confirm Deletion

ALWAYS confirm before deleting. Show:
- Memory summary/content preview
- Category, tags, priority
- Created date and revision count

For bulk operations, show:
- Count of memories that will be deleted
- Category or tag filter being applied

### Step 3: Execute

Single delete:
```
mcp__deepmind__forget(id: "<memory-id>")
```

Bulk delete:
```
mcp__deepmind__bulk_forget(
  category: "<if specified>",
  tag: "<if specified>"
)
```

### Step 4: Report

Confirm what was deleted:
- Memory ID(s)
- Count of deleted chunks, revisions, links, embeddings
