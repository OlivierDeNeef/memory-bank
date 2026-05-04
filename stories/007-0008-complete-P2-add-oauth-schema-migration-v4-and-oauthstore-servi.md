---
id: 007-0008
title: Add OAuth schema migration v4 and OAuthStore service
status: complete
priority: P2
created: "2026-05-04T21:07:27.929Z"
updated: "2026-05-04T21:18:40.002Z"
dependencies: []
---

# Add OAuth schema migration v4 and OAuthStore service

## Problem Statement

Add SQLite migration v4 with `oauth_clients`, `oauth_codes`, `oauth_tokens`, `oauth_refresh_tokens` tables. Add `OAuthStore` service in `MemoryBank.Core` that both Server and Web can use to issue/validate codes, access tokens (1h TTL), and refresh tokens (30d sliding TTL). Add expiry cleanup that runs on each lookup. Add unit tests covering happy path + expiry rejection.

## Acceptance Criteria

- [ ] Implement as described

## Work Log

### 2026-05-04T21:18:36.603Z - Implemented schema v4, OAuthStore, PasswordHasher; 19 OAuth tests + 39 storage tests pass on commit 0294523

