---
name: deepmind:todo
description: Create, list, and complete to-do items stored in DeepMind. Use when the user wants to track tasks, reminders, or action items.
user-invocable: true
argument-hint: "<task description> [--due <date>] [--priority <1-5>] [--tags <t1,t2>]"
---

# DeepMind Todo

Manage personal to-do items stored as DeepMind memories in the `todos` category.

## When Invoked

1. **`/deepmind:todo <task>`** — Create a new todo
2. **`/deepmind:todo list`** — Show open todos
3. **`/deepmind:todo done <search>`** — Mark a todo as complete (archives it)
4. **`/deepmind:todo` (no args)** — Show open todos
5. **AI auto-invokes** — When user says "set a todo for", "remind me to", "add to my list", "I need to"

## Process: Create Todo

### Step 1: Parse the Request

Extract from the user's message:
- **task** — The action item description (required)
- **due** — Due date if mentioned (convert relative dates to absolute, e.g. "tomorrow" → "2026-04-15")
- **priority** — 1 (trivial) to 5 (critical), default 3
- **tags** — Extract relevant keywords (e.g. client names, project names)

If the user mentions multiple todos in one message, create each one separately.

### Step 2: Check for Duplicates

Search for similar existing todos:

```
mcp__deepmind__recall(query: "<task summary>", category: "todos", limit: 3)
```

If a very similar open todo exists, ask the user whether to update or create new.

### Step 3: Store the Todo

Format the content with a clear structure:

```
mcp__deepmind__remember(
  content: "TODO: <task description>\nStatus: open\nDue: <date or 'none'>\nCreated: <today's date>",
  summary: "<concise task summary>",
  category: "todos",
  tags: "<relevant tags, always include 'todo'>",
  type: "fact",
  priority: <1-5>,
  source: "conversation",
  metadata: "{\"status\":\"open\",\"due\":\"<date or null>\",\"created\":\"<today>\"}"
)
```

### Step 4: Confirm

Report back concisely:
- What was saved
- Due date (if any)
- Priority level

## Process: List Todos

Query open todos:

```
mcp__deepmind__recall(query: "TODO status open", category: "todos", limit: 50, sort: "priority")
```

Display as a formatted list:
- Sort by priority (highest first), then by due date
- Show: task, due date, priority, tags
- Indicate overdue items if due date has passed

## Process: Complete Todo

### Step 1: Find the Todo

```
mcp__deepmind__recall(query: "<search term>", category: "todos", limit: 5)
```

If multiple matches, ask user to pick one.

### Step 2: Archive It

```
mcp__deepmind__update_memory(id: "<memory_id>", content: "<original content with Status changed to 'done'>")
mcp__deepmind__archive(id: "<memory_id>")
```

Confirm what was completed.
