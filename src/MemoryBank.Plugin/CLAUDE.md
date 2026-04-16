# MemoryBank Plugin

## IMPORTANT: MemoryBank MCP Tool Access Rules

Two rules govern how MemoryBank MCP tools (`mcp__memorybank__*`) may be used:

### Rule 1: Skills are the only entry point

Never call `mcp__memorybank__*` tools directly. Always go through the corresponding skill:

| Action | Skill to use |
|---|---|
| Store memories | `memorybank:remember` |
| Search/recall | `memorybank:recall` |
| Delete memories | `memorybank:forget` |
| Advanced search | `memorybank:search` |
| Statistics/health | `memorybank:stats` |
| Task management | `memorybank:todo` |

For operations without a skill equivalent (e.g., `link_memories`, `archive`, `unarchive`, `bulk_*`, `get_revisions`, `restore_revision`, `pin`, `unpin`), direct MCP calls are allowed but must still follow Rule 2.

### Rule 2: Entire skill execution runs in a single subagent  ! important !

When a skill is invoked, spawn a **single subagent** (via the Agent tool) that executes the **entire workflow** — parsing, MCP calls, chunk fetching, duplicate checks, everything. The main agent only receives the subagent's final result and presents it cleanly to the user. No MCP calls, no intermediate steps, no raw JSON should ever appear in the main conversation.
