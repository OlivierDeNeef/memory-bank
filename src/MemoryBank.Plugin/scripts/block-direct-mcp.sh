#!/bin/bash
#
# PreToolUse hook: blocks direct mcp__memorybank__* calls.
# Shipped with the MemoryBank plugin — scoped to skill lifecycle.
# Subagents spawned by the skill are separate contexts, so this
# only fires in the main agent where direct calls should be blocked.
#

cat <<'EOF'
{
  "hookSpecificOutput": {
    "hookEventName": "PreToolUse",
    "permissionDecision": "block",
    "permissionDecisionReason": "MemoryBank MCP tools cannot be called directly. This skill must delegate all MCP calls to a subagent (Agent tool). Spawn a subagent with the MCP instructions from this skill, then present the results cleanly."
  }
}
EOF
