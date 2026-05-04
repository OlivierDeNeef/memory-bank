#!/bin/bash
#
# PreToolUse hook: blocks Write/Edit on Claude Code's built-in auto-memory files.
# MemoryBank is the single source of truth for memory — use the memorybank:* skills
# (remember / recall / forget) instead of ~/.claude/projects/*/memory/*.md.
#

input=$(cat)

# Match file_path values that live under any Claude auto-memory directory.
# Handles both POSIX and Windows-style paths in the JSON payload.
if echo "$input" | grep -qE '"file_path"[[:space:]]*:[[:space:]]*"[^"]*[/\\]+\.claude[/\\]+projects[/\\]+[^"]*[/\\]+memory[/\\]+'; then
  cat <<'EOF'
{
  "hookSpecificOutput": {
    "hookEventName": "PreToolUse",
    "permissionDecision": "block",
    "permissionDecisionReason": "Built-in auto-memory is disabled. Use MemoryBank instead: the memorybank:remember skill to save, memorybank:recall to search, memorybank:forget to delete. Do not write to ~/.claude/projects/*/memory/."
  }
}
EOF
  exit 0
fi

exit 0
