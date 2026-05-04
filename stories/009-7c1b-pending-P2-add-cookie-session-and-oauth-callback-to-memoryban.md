---
id: "009-7c1b"
title: "Add cookie session and OAuth callback to MemoryBank.Web"
status: pending
priority: P2
created: 2026-05-04T21:07:27.940Z
updated: 2026-05-04T21:07:27.940Z
dependencies: []
---

# Add cookie session and OAuth callback to MemoryBank.Web

## Problem Statement

Add middleware on `MemoryBank.Web` that redirects unauthenticated browsers to the auth server's `/oauth/authorize` (PKCE-aware). Add `/auth/callback` endpoint that exchanges the code for tokens via `/oauth/token`, stores the access token in `oauth_tokens` (already keyed by session ID), and sets an `HttpOnly; Secure; SameSite=Lax` cookie carrying that session ID. Gate `/api/*` and the SPA root behind the cookie. Add `/auth/logout` that deletes the session row and clears the cookie. Depends on OAuthStore + real login stories.

## Acceptance Criteria

- [ ] Implement as described

## Work Log

