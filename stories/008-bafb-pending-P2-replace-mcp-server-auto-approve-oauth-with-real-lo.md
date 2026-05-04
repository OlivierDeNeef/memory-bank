---
id: "008-bafb"
title: "Replace MCP server auto-approve OAuth with real login + token validation"
status: pending
priority: P2
created: 2026-05-04T21:07:27.934Z
updated: 2026-05-04T21:07:27.934Z
dependencies: []
---

# Replace MCP server auto-approve OAuth with real login + token validation

## Problem Statement

Rewrite `OAuthEndpoints.cs`: GET `/oauth/authorize` renders a login form preserving PKCE/state/redirect_uri/client_id; POST `/oauth/authorize` validates `MEMORYBANK_AUTH_USERNAME`/`MEMORYBANK_AUTH_PASSWORD_HASH` (PBKDF2-SHA256, 100k iterations) and issues an auth code via `OAuthStore`. Add `/oauth/token` refresh-token grant. Add token-validation middleware on `/mcp` that returns 401 with `WWW-Authenticate: Bearer resource_metadata=...` when missing/invalid. Add `dotnet run -- --hash-password <pw>` CLI helper. Depends on schema/OAuthStore story.

## Acceptance Criteria

- [ ] Implement as described

## Work Log

