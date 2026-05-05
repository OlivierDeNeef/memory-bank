---
id: 010-3cc6
title: Add nginx config, docker-compose override, and deployment docs
status: complete
priority: P2
created: "2026-05-04T21:07:27.943Z"
updated: "2026-05-04T21:37:35.365Z"
dependencies: []
---

# Add nginx config, docker-compose override, and deployment docs

## Problem Statement

Create `deploy/nginx/memory-bank.conf` for `memory-bank.dno-ontwikkeling.com` routing `/`, `/api/*`, `/auth/callback` to viewer (`127.0.0.1:5174`) and `/mcp`, `/oauth/*`, `/.well-known/*` to MCP server (`127.0.0.1:6868`), with TLS via Let's Encrypt and SSE-friendly proxy settings on `/mcp`. Create `docker-compose.override.yml` binding both ports to `127.0.0.1` only and setting auth env vars. Update README with the deployment flow, the `--hash-password` setup, the per-device `claude mcp add` command (no `--header` — browser login), and a brief rollback note. Depends on the previous three stories.

## Acceptance Criteria

- [ ] Implement as described

## Work Log

### 2026-05-04T21:37:35.133Z - docker-compose.override.yml + .env.example + README hosting walkthrough (NPM proxy host, custom locations, SSE settings, Let's Encrypt, per-device claude mcp add, rollback). Troubleshooting entries added on commit a827e0e

