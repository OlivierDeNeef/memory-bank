---
name: todo
description: Create, list, and complete to-do items stored in MemoryBank. Use when the user wants to track tasks, reminders, or action items.
user-invocable: true
argument-hint: "<task description> [--due <date>] [--priority <1-5>] [--tags <t1,t2>]"
---

# MemoryBank Todo

Manage personal to-do items stored as MemoryBank memories in the `todos` category.

## When Invoked

1. **`/todo <task>`** — Create a new todo
2. **`/todo list`** — Show open todos
3. **`/todo done <search>`** — Mark a todo as complete (archives it)
4. **`/todo` (no args)** — Show open todos
5. **AI auto-invokes** — When user says "set a todo for", "remind me to", "add to my list", "I need to"

## Process: Create Todo

### Step 1: Parse the Request (main agent)

Extract from the user's message:
- **task** — The action item description (required)
- **due** — Due date if mentioned (convert relative dates to absolute, e.g. "tomorrow" → "2026-04-16")
- **priority** — 1 (trivial) to 5 (critical), default 3
- **tags** — Extract relevant keywords (e.g. client names, project names)

If the user mentions multiple todos in one message, create each one separately.

### Step 2: Spawn a subagent (main agent)  ! important !

Use the **Agent tool** to spawn a single subagent. Pass it a prompt with the parsed task details and the instructions below. Do NOT call any `mcp__memorybank__*` tools yourself.

**Subagent prompt must include:**
- The task description, due date, priority, and tags
- The subagent instructions from Step 3

### Step 3: Subagent instructions (create)

> These instructions are for the subagent, include them in the Agent tool prompt.

**Check duplicates:** Call `mcp__memorybank__recall(query: "<task summary>", category: "todos", limit: 3)`. If a very similar open todo exists, return it so the main agent can ask the user.

**Store:** Call `mcp__memorybank__remember` with: content formatted as "TODO: <task>\nStatus: open\nDue: <date or 'none'>\nCreated: <today>", summary: "<concise task summary>", category: "todos", tags: "<tags, always include 'todo'>", type: "fact", priority: <1-5>, source: "conversation".

**Return:** The stored todo's title, due date, priority, tags, and whether a duplicate was found. Exclude memory IDs, chunk info, raw JSON.

### Step 4: Present result (main agent)

> **Todo added**
> <task description>
> **Due:** <date or "No deadline"> · **Priority:** <label>
> **Tags:** <tags>

## Process: List Todos

### Step 1: Spawn a subagent (main agent)

Use the **Agent tool**. Do NOT call any `mcp__memorybank__*` tools yourself.

**Subagent prompt:** Call `mcp__memorybank__recall(query: "TODO status open", category: "todos", limit: 50, sort: "priority")`. Return each todo's title/task, due date, priority label, tags, and status. Exclude memory IDs, similarity scores, chunk info, raw JSON.

### Step 2: Present results (main agent)

Display as a formatted checklist, sorted by priority (highest first), then by due date:

> ### Open Todos
>
> - [ ] **<task>** — Due: <date> · Priority: <label> · <tags>
> - [ ] **<task>** — Due: <date> · Priority: <label> · <tags>
> - [ ] **<task>** — Overdue (<date>) · Priority: <label>

Mark overdue items with a warning. If no open todos, say "No open todos."

## Process: Complete Todo

### Step 1: Spawn a search subagent (main agent)

Use the **Agent tool**. Do NOT call any `mcp__memorybank__*` tools yourself.

**Subagent prompt:** Call `mcp__memorybank__recall(query: "<search term>", category: "todos", limit: 5)`. Return each match's memory ID, task title, due date, and priority. Exclude raw JSON and chunk metadata.

### Step 2: Confirm (main agent)

If multiple matches, present them cleanly numbered and ask the user to pick one.

### Step 3: Spawn a completion subagent (main agent)

After user confirms, use the **Agent tool**. Do NOT call any `mcp__memorybank__*` tools yourself.

**Subagent prompt:** Call `mcp__memorybank__update_memory(id: "<id>", content: "<content with Status changed to 'done'>")` then `mcp__memorybank__archive(id: "<id>")`. Return confirmation with the task title.

### Step 4: Confirm (main agent)

> **Completed:** <task description>
