# MemoryBank — Application Specification

> A persistent, searchable memory system exposed as an MCP server.
> MemoryBank acts as a "second mind" — an external knowledge store that any AI client can use to remember, recall, and manage information across sessions.

## Core Principle

> **MemoryBank is a memory, not a brain.** The brain is whatever AI is talking to it.

MemoryBank contains no AI inference. It is a fast, reliable, well-indexed store. The calling AI (Claude, Copilot, Cursor, etc.) handles all reasoning, summarization, and decision-making. MemoryBank stores and retrieves.

---

## 1. Architecture

```
AI Client (Claude Code, Cursor, Copilot, etc.)
    | MCP Protocol (stdio transport)
    v
MemoryBank MCP Server (C# / .NET)
    |
    v
SQLite (single local file)
    |- FTS5 (full-text keyword search)
    |- sqlite-vec (vector similarity search)
    '- Relational tables (structured queries)
```

### Deployment Model

- **Local only**, single device, single user
- No server process — launched as a child process by the AI client via stdio
- No network, no API keys, no Docker, no infrastructure
- All data in a single `.db` file on disk

### MCP Client Configuration

```json
{
  "mcpServers": {
    "memorybank": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/path/to/MemoryBank.Server"]
    }
  }
}
```

---

## 2. Technology Stack

### Language & Runtime

- **C# / .NET 8+**

### NuGet Packages

| Package | Purpose |
|---------|---------|
| `ModelContextProtocol` | MCP server SDK |
| `Microsoft.Data.Sqlite` | SQLite access |
| `Microsoft.ML.OnnxRuntime` | Local embedding model inference |
| `Microsoft.Extensions.Hosting` | Dependency injection, configuration, logging |

### Storage

- **SQLite** with WAL mode enabled
- **FTS5** for full-text keyword search with stemming
- **sqlite-vec** extension for vector similarity search

### Embeddings

- Local ONNX models, no external API calls
- Candidate models: `all-MiniLM-L6-v2` (80 MB), `nomic-embed-text-v1.5` (270 MB), `bge-small-en-v1.5` (130 MB)
- Configurable model selection

---

## 3. Project Structure

```
MemoryBank/
├── MemoryBank.sln
├── src/
│   ├── MemoryBank.Server/           -- MCP server entry point
│   │   ├── Program.cs
│   │   ├── Tools/
│   │   │   ├── RememberTool.cs
│   │   │   ├── RecallTool.cs
│   │   │   ├── ForgetTool.cs
│   │   │   ├── ManageTool.cs
│   │   │   └── CategoryTool.cs
│   │   └── MemoryBank.Server.csproj
│   └── MemoryBank.Core/             -- Models, storage, search
│       ├── Models/
│       │   ├── Memory.cs
│       │   ├── Chunk.cs
│       │   ├── Category.cs
│       │   ├── Tag.cs
│       │   ├── Revision.cs
│       │   └── MemoryLink.cs
│       ├── Storage/
│       │   ├── MemoryBankDb.cs
│       │   └── Migrations/
│       ├── Search/
│       │   ├── HybridSearchEngine.cs
│       │   ├── VectorSearch.cs
│       │   └── KeywordSearch.cs
│       ├── Embeddings/
│       │   └── OnnxEmbeddingService.cs
│       └── MemoryBank.Core.csproj
└── tests/
    └── MemoryBank.Tests/
```

---

## 4. Data Model

### 4.1 Memory Document

A memory is the core unit of knowledge:

```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "content": "Auth service uses JWT with ES256 signing",
  "summary": "Auth service JWT configuration",
  "category": "architecture/auth",
  "type": "decision",
  "tags": ["auth", "jwt", "security"],
  "priority": 4,
  "isPinned": false,
  "isArchived": false,
  "accessCount": 7,
  "revisionNumber": 3,
  "source": "conversation",
  "validFrom": null,
  "validUntil": null,
  "metadata": {
    "project": "backend",
    "confidence": "high"
  },
  "createdAt": "2026-03-01T10:00:00Z",
  "updatedAt": "2026-04-10T09:00:00Z",
  "lastAccessed": "2026-04-14T12:30:00Z"
}
```

### 4.2 Memory Types

| Type | Purpose | Example |
|------|---------|---------|
| `fact` | A discrete piece of knowledge | "API rate limit is 1000/min" |
| `decision` | A choice that was made and why | "We chose PostgreSQL because..." |
| `procedure` | Step-by-step instructions | "How to deploy to staging" |
| `reference` | Pointer to external resource | URL, file path, doc link |
| `observation` | Subjective note or pattern | "This module tends to break after deployments" |

### 4.3 Priority System

Composite importance model:

```
Effective Score = base_priority x recency_boost x access_frequency + pin_bonus
```

| Factor | How it works |
|--------|-------------|
| **Base priority** | Set at creation: `critical` (5), `high` (4), `normal` (3), `low` (2), `trivial` (1) |
| **Recency boost** | Decays over time — recent memories rank higher unless pinned |
| **Access frequency** | Memories recalled often get boosted |
| **Pinned** | Boolean override — never decays, always surfaces when relevant |

### 4.4 Revision History

Every update to a memory creates a revision snapshot:

```json
{
  "revisionNumber": 2,
  "content": "Auth service uses JWT with RS256 signing",
  "summary": "Auth JWT with RS256",
  "reason": "migrated to JWT",
  "timestamp": "2026-03-15T14:00:00Z"
}
```

Current content is always top-level on the memory for fast read. Revisions are stored in a separate table, indexed, and never slow down the main query.

### 4.5 Chunked Storage for Large Content

When storing large content (e.g., an entire conversation), MemoryBank automatically splits it into searchable chunks while preserving the full original:

```
Parent memory (full content stored, not indexed)
    |
    ├── Chunk 1 (embedded + indexed)
    ├── Chunk 2 (embedded + indexed)
    ├── ...
    └── Chunk N (embedded + indexed)
```

| Factor | Approach |
|--------|----------|
| Chunk size | ~300-500 tokens (fits embedding model context) |
| Overlap | 50 tokens overlap between chunks |
| Split boundaries | Prefer paragraph/sentence breaks |
| Small memories | Under 500 tokens = single chunk, no splitting |

### 4.6 Memory Relationships

Memories can be linked with typed relationships:

| Link Type | Meaning |
|-----------|---------|
| `related` | General association |
| `supersedes` | New memory replaces old one |
| `contradicts` | Conflicting information |
| `extends` | Adds detail to another memory |

---

## 5. Database Schema

```sql
-- Core memory storage
CREATE TABLE memories (
    id              TEXT PRIMARY KEY,
    content         TEXT NOT NULL,
    summary         TEXT,
    category_id     TEXT REFERENCES categories(id),
    type            TEXT CHECK(type IN ('fact','decision','procedure','reference','observation')),
    priority        INTEGER NOT NULL DEFAULT 3,
    is_pinned       INTEGER NOT NULL DEFAULT 0,
    is_archived     INTEGER NOT NULL DEFAULT 0,
    access_count    INTEGER NOT NULL DEFAULT 0,
    revision_number INTEGER NOT NULL DEFAULT 1,
    token_count     INTEGER,
    valid_from      TEXT,
    valid_until     TEXT,
    source          TEXT,
    metadata        TEXT,    -- JSON
    created_at      TEXT NOT NULL,
    updated_at      TEXT NOT NULL,
    last_accessed   TEXT
);

-- Revision history
CREATE TABLE revisions (
    id              TEXT PRIMARY KEY,
    memory_id       TEXT NOT NULL REFERENCES memories(id) ON DELETE CASCADE,
    revision_number INTEGER NOT NULL,
    content         TEXT NOT NULL,
    summary         TEXT,
    reason          TEXT,
    created_at      TEXT NOT NULL,
    UNIQUE(memory_id, revision_number)
);

-- Chunks for large memories
CREATE TABLE chunks (
    id              TEXT PRIMARY KEY,
    memory_id       TEXT NOT NULL REFERENCES memories(id) ON DELETE CASCADE,
    chunk_index     INTEGER NOT NULL,
    content         TEXT NOT NULL,
    summary         TEXT,
    token_count     INTEGER,
    created_at      TEXT NOT NULL,
    UNIQUE(memory_id, chunk_index)
);

-- Vector embeddings (one per chunk)
CREATE TABLE embeddings (
    id              TEXT PRIMARY KEY,
    chunk_id        TEXT REFERENCES chunks(id) ON DELETE CASCADE,
    memory_id       TEXT NOT NULL REFERENCES memories(id) ON DELETE CASCADE,
    embedding       BLOB NOT NULL,
    model           TEXT NOT NULL,
    created_at      TEXT NOT NULL
);

-- Hierarchical categories
CREATE TABLE categories (
    id              TEXT PRIMARY KEY,
    name            TEXT NOT NULL,
    parent_id       TEXT REFERENCES categories(id),
    description     TEXT
);

-- Tags
CREATE TABLE tags (
    id              TEXT PRIMARY KEY,
    name            TEXT NOT NULL UNIQUE
);

CREATE TABLE memory_tags (
    memory_id       TEXT REFERENCES memories(id) ON DELETE CASCADE,
    tag_id          TEXT REFERENCES tags(id),
    PRIMARY KEY (memory_id, tag_id)
);

-- Relationships between memories
CREATE TABLE memory_links (
    source_id       TEXT REFERENCES memories(id) ON DELETE CASCADE,
    target_id       TEXT REFERENCES memories(id) ON DELETE CASCADE,
    link_type       TEXT NOT NULL,
    PRIMARY KEY (source_id, target_id)
);

-- Audit log
CREATE TABLE audit_log (
    id              TEXT PRIMARY KEY,
    memory_id       TEXT,
    action          TEXT NOT NULL,  -- 'created', 'updated', 'deleted', 'recalled', 'archived'
    details         TEXT,           -- JSON
    created_at      TEXT NOT NULL
);

-- Full-text search (indexes chunks, not full documents)
CREATE VIRTUAL TABLE chunks_fts USING fts5(
    content,
    summary,
    content_rowid='rowid',
    tokenize='porter unicode61'
);

-- Indexes
CREATE INDEX idx_memories_category ON memories(category_id);
CREATE INDEX idx_memories_priority ON memories(priority);
CREATE INDEX idx_memories_type ON memories(type);
CREATE INDEX idx_memories_created ON memories(created_at);
CREATE INDEX idx_memories_archived ON memories(is_archived);
CREATE INDEX idx_revisions_memory ON revisions(memory_id, revision_number);
CREATE INDEX idx_chunks_memory ON chunks(memory_id, chunk_index);
CREATE INDEX idx_embeddings_memory ON embeddings(memory_id);
CREATE INDEX idx_embeddings_chunk ON embeddings(chunk_id);
CREATE INDEX idx_tags_name ON tags(name);
CREATE INDEX idx_audit_memory ON audit_log(memory_id);
CREATE INDEX idx_audit_action ON audit_log(action);
```

---

## 6. Search System

### 6.1 Hybrid Search

Three search modes combined:

```
User query
    |
    v
Generate embedding (ONNX, local, ~5ms)
    |
    +--> Vector search (semantic similarity via sqlite-vec)
    |
    +--> Keyword search (FTS5 with stemming)
    |
    +--> Structured filters (category, tags, priority, type, date)
    |
    v
Merge & rank by weighted composite score
```

### 6.2 Search Result Scoring

```
FinalScore = (vector_weight x VectorScore)
           + (keyword_weight x KeywordScore)
           + (priority_weight x PriorityScore)
           + (pin_bonus)
           - (recency_decay)
```

Weights are configurable.

### 6.3 Search Result Format

Small memory — returned directly:

```json
{
  "id": "...",
  "content": "Auth uses JWT with ES256",
  "summary": "Auth JWT config",
  "type": "fact",
  "priority": 4,
  "score": 0.92
}
```

Large memory — returns matching chunk with pointer to full document:

```json
{
  "id": "...",
  "matchedChunk": "We chose SQLite over LiteDB because...",
  "chunkIndex": 12,
  "parentSummary": "MemoryBank app design session - April 2026",
  "totalChunks": 15,
  "hint": "Call get_memory or get_chunks for full context"
}
```

### 6.4 Query Capabilities

- Full-text keyword search with stemming ("deploy" matches "deployment")
- Semantic/vector similarity search ("login tokens" matches "authentication JWT")
- Filter by category (including children)
- Filter by tags (AND/OR)
- Filter by date range
- Filter by priority threshold
- Filter by memory type
- Negation filters ("auth NOT jwt")
- Sort by: relevance, date, priority, access count, revision count
- Count queries without returning content
- Exists check (boolean)
- Fuzzy matching and partial matches
- Pagination with limit/offset

---

## 7. MCP Interface

### 7.1 Tools

| Tool | Parameters | Returns |
|------|-----------|---------|
| `remember` | content, category?, priority?, tags[]?, type?, source?, summary?, metadata? | memory id, auto-generated summary, chunk count |
| `recall` | query, category?, tags[]?, min_priority?, type?, date_from?, date_to?, sort?, limit?, offset? | ranked list of memories/chunks |
| `recall_recent` | hours_back?, category? | time-filtered memories |
| `get_memory` | id | full memory with all fields |
| `get_chunks` | memory_id, range? | chunks of a large memory |
| `update_memory` | id, content?, priority?, tags[]?, category?, type?, reason? | updated memory, new revision created |
| `forget` | id | confirmation |
| `bulk_forget` | category?, tag?, date_before?, filter? | count of deleted memories |
| `pin` | id | confirmation |
| `unpin` | id | confirmation |
| `archive` | id | confirmation |
| `unarchive` | id | confirmation |
| `link_memories` | source_id, target_id, link_type | confirmation |
| `unlink_memories` | source_id, target_id | confirmation |
| `get_linked` | id, link_type? | linked memories |
| `list_categories` | parent_id? | category tree with counts |
| `create_category` | name, parent_id?, description? | category id |
| `rename_category` | id, name | confirmation |
| `delete_category` | id | confirmation (fails if non-empty) |
| `move_memory` | id, category_id | confirmation |
| `list_tags` | sort?, limit? | tags with usage counts |
| `rename_tag` | old_name, new_name | confirmation |
| `merge_tags` | source_tag, target_tag | confirmation |
| `bulk_tag` | memory_ids[], tag | confirmation |
| `bulk_recategorize` | memory_ids[], category_id | confirmation |
| `get_revisions` | memory_id | revision list with timestamps and reasons |
| `get_revision` | memory_id, revision_number | full content of a specific revision |
| `diff_revisions` | memory_id, from_revision, to_revision | changes between revisions |
| `restore_revision` | memory_id, revision_number | restored memory, creates new revision |
| `memory_stats` | none | total counts, DB size, category counts, tag counts |
| `exists` | query, category? | boolean |
| `count` | query?, category?, tags[]?, type? | integer count |
| `export` | category?, tag?, format? | JSON dump |
| `import` | data, conflict_resolution? | import report |
| `backup` | none | backup file path |
| `restore_backup` | path | confirmation |
| `health_check` | none | DB status, file size, index health |

### 7.2 Resources

Loaded once per AI session, not on every call:

| Resource URI | Content |
|--------------|---------|
| `memorybank://index` | Categories with counts, top tags, memory types, total stats, recent activity |

### 7.3 Resource Subscriptions

The server notifies the client when the index resource changes (new categories, significant shifts in tag usage, etc.) so the AI can refresh its knowledge of the store.

---

## 8. Feature List

### Memory Management (Core)
1. Store memory with content, category, tags, priority, type, source, metadata
2. Update memory content and metadata
3. Delete a specific memory by ID
4. Bulk delete by category, tag, date range, or filter
5. Pin/unpin memory (immune to recency decay)
6. Archive/unarchive memory (soft-delete, hidden from search)
7. Duplicate detection on store (warn or merge)

### Search & Recall
8. Full-text keyword search with stemming
9. Semantic vector similarity search
10. Hybrid search combining vector + keyword + priority scoring
11. Search by category (including child categories)
12. Search by tags (AND/OR)
13. Search by date range
14. Search by priority threshold
15. Search by memory type
16. Combined multi-filter search
17. Fuzzy matching and partial matches
18. Negation filters
19. Ranked results by configurable composite score
20. Pagination with limit/offset
21. Count query (without returning content)
22. Exists check (boolean)
23. Sort options: relevance, date, priority, access count, revision count
24. Natural language recall (handled by calling AI, enabled by good search tools)

### Organization
25. Hierarchical categories (nested tree: `projects/backend/auth`)
26. Create, rename, delete categories
27. Move memory between categories
28. Tag management: list, rename, merge duplicate tags
29. Auto-tagging suggestions based on content keywords (done by calling AI)
30. Auto-categorization suggestions (done by calling AI using the index resource)

### Relationships & Context
31. Link memories with typed relationships (related, supersedes, contradicts, extends)
32. Unlink memories
33. Find related/linked memories
34. Supersede chains (history trail of updated knowledge)
35. Conflict detection (flagging contradictions)

### Priority & Importance System
36. Manual priority setting (1-5: trivial to critical)
37. Access count tracking (incremented on recall)
38. Recency decay (configurable decay rate)
39. Auto-boost for frequently accessed memories
40. Pin override (bypasses decay)
41. Priority distribution summary

### Revision History
42. Automatic revision snapshot on every update
43. Revision log with timestamps and change reasons
44. View full content of any past revision
45. Diff between two revisions
46. Restore a previous revision (creates new revision)
47. Change reason field on update
48. Revision count exposed on memory
49. Search for memories revised within a date range

### Chunked Storage (Large Content)
50. Automatic chunking for content exceeding token threshold
51. Configurable chunk size and overlap
52. Smart splitting at sentence/paragraph boundaries
53. Chunk-level vector embeddings and FTS indexing
54. Parent memory retrieval from chunk match
55. Surrounding chunk retrieval for context
56. Per-chunk summaries
57. Token counting per chunk and per memory
58. Re-chunking on content update

### Memory Types
59. Fact, decision, procedure, reference, observation types
60. Type filtering in search

### Temporal Intelligence
61. Validity period (optional valid_from / valid_until)
62. Expired memory handling (flag or auto-archive)
63. Freshness indicator on recall results
64. Temporal context signaling (age + type = confidence hint)

### Vector Embeddings
65. Local ONNX embedding model (no API calls)
66. Embedding generation on store
67. Embedding regeneration on update
68. Batch re-embedding (model upgrade migration)
69. Configurable embedding model selection
70. Query embedding cache

### Search Scoring
71. Configurable weights for vector, keyword, and priority scores
72. Pin bonus in ranking
73. Recency decay in ranking
74. Access frequency boost in ranking

### Bulk Operations
75. Bulk import with conflict resolution (skip, overwrite, merge)
76. Bulk tag (add/remove tags on multiple memories)
77. Bulk recategorize
78. Bulk priority update

### Import / Export
79. Export all to JSON
80. Export filtered subset (by category, tag)
81. Import from JSON
82. Import from Markdown
83. Import from plain text (unstructured)

### Data Integrity & Maintenance
84. Automatic periodic backups of the .db file
85. Restore from backup
86. Database compaction (VACUUM)
87. Duplicate detection and merge tool
88. Audit log (created, updated, deleted, recalled events with timestamps)
89. Content validation (reject empty/meaningless content)
90. Max content size limit (configurable)
91. Category depth limit
92. Orphan cleanup (tags/categories with no memories)
93. Referential integrity on delete (handle links, revisions, chunks)

### MCP Server
94. stdio transport for local communication
95. Tool descriptions with usage examples for AI discovery
96. Structured, consistent response format across all tools
97. Clear error messages the AI can interpret and act on
98. Graceful handling of rapid successive calls

### MCP Resources & Discovery
99. Knowledge index resource (categories, top tags, types, stats)
100. Category discovery tool with hierarchy and counts
101. Tag discovery tool sorted by frequency
102. Memory stats tool (counts, file size, last activity)
103. Recent activity resource
104. Resource subscription for index changes

### Configuration
105. DB file location (configurable path)
106. Backup location
107. Default priority for new memories
108. Recency decay rate
109. Default result page size
110. Chunk size and overlap settings
111. Embedding model path
112. Search weight tuning (vector vs keyword vs priority)

### Observability
113. Operation logging (every tool call with timestamp, params, result count)
114. Query performance metrics
115. Health check tool (DB accessible, file size, index health)

### Database
116. SQLite with FTS5 for full-text search
117. sqlite-vec for vector similarity search
118. WAL mode for concurrent read performance
119. Automatic FTS sync via triggers
120. Database migration system for schema versioning
121. Periodic ANALYZE and OPTIMIZE on FTS and indexes

---

## 9. Scalability

### Growth Projections

| Timeframe | Memories | Estimated DB Size | Performance |
|-----------|----------|-------------------|-------------|
| Year 1 | ~5,000 | ~10 MB | Instant |
| Year 3 | ~50,000 | ~100 MB | Instant |
| Year 10 | ~500,000 | ~1 GB | Fast with indexes |

SQLite is proven to handle this scale. FTS5 and sqlite-vec are designed for efficient querying at these volumes.

### Chunked storage prevents document bloat from degrading search. Revisions in a separate table prevent memory reads from slowing as history grows.

---

## 10. Usage Example

### Conversation 1 — Storing knowledge

> **User:** We decided to use MediatR for CQRS in the ordering service. The API will be REST with versioned endpoints.
>
> **AI calls:** `remember(content: "Ordering service uses MediatR for CQRS pattern. API is REST with versioned endpoints.", category: "projects/ordering-service", tags: ["architecture", "mediatr", "cqrs", "rest"], priority: 4, type: "decision")`

### Conversation 2 — Recalling in a different session

> **User:** I need to add a new endpoint to the ordering service. What patterns are we using?
>
> **AI calls:** `recall(query: "ordering service patterns architecture", category: "projects/ordering-service")`
>
> **AI responds:** Based on your earlier decision, you're using MediatR for CQRS with versioned REST endpoints.

### Conversation 3 — Updating knowledge (creates revision)

> **User:** We dropped MediatR, too much boilerplate. Going with minimal APIs.
>
> **AI calls:** `update_memory(id: "...", content: "Ordering service uses minimal APIs with simple service classes. MediatR/CQRS was dropped due to boilerplate overhead.", reason: "architecture simplification")`

### Conversation 4 — Storing a large document

> **User:** Save this entire conversation as a memory.
>
> **AI calls:** `remember(content: "...entire conversation text...", category: "sessions/design", tags: ["memorybank", "architecture"], type: "reference", summary: "MemoryBank app design session - MCP server, features, storage decisions")`
>
> MemoryBank auto-chunks the content, embeds each chunk, and stores the full original for retrieval.

---

## 11. Error Handling

### 11.1 Error Response Format

All tool errors return a consistent structure:

```json
{
  "success": false,
  "error": {
    "code": "MEMORY_NOT_FOUND",
    "message": "No memory found with id '550e8400-...'",
    "details": null
  }
}
```

### 11.2 Error Codes

| Code | HTTP Analogy | When |
|------|-------------|------|
| `MEMORY_NOT_FOUND` | 404 | ID does not exist |
| `CATEGORY_NOT_FOUND` | 404 | Category ID does not exist |
| `TAG_NOT_FOUND` | 404 | Tag name does not exist |
| `REVISION_NOT_FOUND` | 404 | Revision number does not exist for memory |
| `VALIDATION_FAILED` | 400 | Content empty, priority out of range, invalid type, etc. |
| `DUPLICATE_DETECTED` | 409 | Memory with very similar content already exists |
| `CATEGORY_NOT_EMPTY` | 409 | Cannot delete category that still contains memories |
| `CATEGORY_DEPTH_EXCEEDED` | 400 | Category nesting exceeds configured max depth |
| `CONTENT_TOO_LARGE` | 400 | Content exceeds configured max size |
| `INVALID_LINK_TYPE` | 400 | Link type not in allowed set |
| `SELF_LINK` | 400 | Cannot link a memory to itself |
| `BACKUP_FAILED` | 500 | Could not create backup (disk full, permissions) |
| `RESTORE_FAILED` | 500 | Backup file not found, corrupt, or incompatible schema version |
| `DB_CORRUPTED` | 500 | SQLite integrity check failed |
| `EMBEDDING_FAILED` | 500 | ONNX model failed to generate embedding |
| `MODEL_NOT_FOUND` | 500 | Configured ONNX model file not found on disk |
| `IMPORT_FAILED` | 500 | Import data malformed or conflict resolution failed |
| `UNKNOWN_ERROR` | 500 | Unexpected internal error |

### 11.3 Error Behavior

- **Invalid input**: Return error immediately, never partially execute.
- **DB write failure**: All writes are wrapped in transactions. On failure, roll back entirely and return error.
- **Embedding failure**: Store the memory without embedding. Flag it with `embedding_status: "failed"` in metadata. The memory is still keyword-searchable. Log a warning.
- **FTS sync failure**: Same approach — memory is stored, FTS index is flagged as stale. `health_check` will report it.
- **Corrupt database**: `health_check` runs `PRAGMA integrity_check`. If corruption is detected, return `DB_CORRUPTED` with instructions to restore from backup.

---

## 12. API Contracts

### 12.1 Success Response Format

All successful tool responses follow this structure:

```json
{
  "success": true,
  "data": { ... },
  "meta": {
    "duration_ms": 12,
    "timestamp": "2026-04-14T10:00:00Z"
  }
}
```

### 12.2 Tool Contracts

#### `remember`

**Input:**
```json
{
  "content": "string, required, 1-100000 chars",
  "summary": "string, optional, max 500 chars. If omitted, auto-truncated from content.",
  "category": "string, optional, slash-separated path e.g. 'projects/backend'. Created if not exists.",
  "priority": "integer, optional, 1-5, default 3",
  "tags": "string[], optional, each tag 1-100 chars, max 50 tags",
  "type": "string, optional, one of: fact|decision|procedure|reference|observation, default fact",
  "source": "string, optional, max 100 chars, e.g. 'conversation', 'manual', 'import'",
  "metadata": "object, optional, arbitrary JSON, max 10KB serialized"
}
```

**Output:**
```json
{
  "success": true,
  "data": {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "summary": "Auth service JWT configuration",
    "chunkCount": 1,
    "duplicateWarning": null
  }
}
```

**Output (duplicate detected):**
```json
{
  "success": true,
  "data": {
    "id": "550e8400-...",
    "summary": "Auth service JWT configuration",
    "chunkCount": 1,
    "duplicateWarning": {
      "existingId": "660f9500-...",
      "existingSummary": "Auth service uses JWT tokens",
      "similarity": 0.91
    }
  }
}
```

#### `recall`

**Input:**
```json
{
  "query": "string, required, 1-1000 chars",
  "category": "string, optional, filter to this category and children",
  "tags": "string[], optional, filter by tags",
  "tag_mode": "string, optional, 'and'|'or', default 'or'",
  "min_priority": "integer, optional, 1-5",
  "type": "string, optional, filter by memory type",
  "date_from": "string, optional, ISO 8601",
  "date_to": "string, optional, ISO 8601",
  "include_archived": "boolean, optional, default false",
  "sort": "string, optional, 'relevance'|'date'|'priority'|'access_count'|'revision_count', default 'relevance'",
  "limit": "integer, optional, 1-100, default 10",
  "offset": "integer, optional, default 0"
}
```

**Output:**
```json
{
  "success": true,
  "data": {
    "results": [
      {
        "id": "550e8400-...",
        "content": "Auth service uses JWT with ES256 signing",
        "summary": "Auth service JWT configuration",
        "category": "architecture/auth",
        "type": "decision",
        "priority": 4,
        "tags": ["auth", "jwt"],
        "isPinned": false,
        "accessCount": 8,
        "revisionNumber": 3,
        "createdAt": "2026-03-01T10:00:00Z",
        "updatedAt": "2026-04-10T09:00:00Z",
        "score": 0.92,
        "scoreBreakdown": {
          "vector": 0.85,
          "keyword": 0.95,
          "priority": 0.80
        },
        "isChunked": false,
        "freshness": "fresh"
      }
    ],
    "totalCount": 42,
    "hasMore": true
  }
}
```

**Output (chunked memory match):**
```json
{
  "success": true,
  "data": {
    "results": [
      {
        "id": "770a1234-...",
        "matchedChunk": "We chose SQLite over LiteDB because of scalability concerns...",
        "chunkIndex": 12,
        "totalChunks": 15,
        "parentSummary": "MemoryBank app design session - April 2026",
        "category": "sessions/design",
        "type": "reference",
        "priority": 3,
        "tags": ["memorybank", "architecture"],
        "score": 0.88,
        "isChunked": true,
        "freshness": "fresh",
        "hint": "Use get_memory or get_chunks for full content"
      }
    ],
    "totalCount": 1,
    "hasMore": false
  }
}
```

#### `update_memory`

**Input:**
```json
{
  "id": "string, required, UUID",
  "content": "string, optional, new content",
  "summary": "string, optional, new summary",
  "priority": "integer, optional, 1-5",
  "tags": "string[], optional, replaces all tags",
  "category": "string, optional, moves to new category",
  "type": "string, optional, new type",
  "metadata": "object, optional, merged with existing",
  "reason": "string, optional, why this update was made, max 500 chars"
}
```

**Output:**
```json
{
  "success": true,
  "data": {
    "id": "550e8400-...",
    "revisionNumber": 4,
    "previousRevision": 3,
    "reason": "architecture simplification",
    "chunksRegenerated": false
  }
}
```

#### `forget`

**Input:**
```json
{
  "id": "string, required, UUID"
}
```

**Output:**
```json
{
  "success": true,
  "data": {
    "id": "550e8400-...",
    "deletedChunks": 0,
    "deletedRevisions": 3,
    "deletedLinks": 1,
    "deletedEmbeddings": 1
  }
}
```

#### `get_revisions`

**Input:**
```json
{
  "memory_id": "string, required, UUID"
}
```

**Output:**
```json
{
  "success": true,
  "data": {
    "memoryId": "550e8400-...",
    "currentRevision": 3,
    "revisions": [
      {
        "revisionNumber": 1,
        "summary": "Auth basic token auth",
        "reason": "initial",
        "createdAt": "2026-03-01T10:00:00Z"
      },
      {
        "revisionNumber": 2,
        "summary": "Auth JWT with RS256",
        "reason": "migrated to JWT",
        "createdAt": "2026-03-15T14:00:00Z"
      },
      {
        "revisionNumber": 3,
        "summary": "Auth JWT with ES256",
        "reason": "switched to ES256 for performance",
        "createdAt": "2026-04-10T09:00:00Z"
      }
    ]
  }
}
```

#### `health_check`

**Input:** none

**Output:**
```json
{
  "success": true,
  "data": {
    "status": "healthy",
    "dbFileSize": "45.2 MB",
    "dbFilePath": "C:/Users/user/.memorybank/memorybank.db",
    "totalMemories": 2847,
    "totalChunks": 4102,
    "totalRevisions": 8934,
    "totalCategories": 34,
    "totalTags": 189,
    "integrityCheck": "ok",
    "ftsStatus": "ok",
    "embeddingModel": "all-MiniLM-L6-v2",
    "embeddingStatus": "ok",
    "pendingReembeddings": 0,
    "lastBackup": "2026-04-14T03:00:00Z",
    "schemaVersion": 3
  }
}
```

---

## 13. Validation Rules

### 13.1 Content

| Field | Rule |
|-------|------|
| `content` | Required. 1–100,000 characters. Must contain at least one non-whitespace character. |
| `summary` | Optional. Max 500 characters. Auto-generated from first 500 chars of content if omitted. |
| `reason` (revision) | Optional. Max 500 characters. |
| `metadata` | Optional. Valid JSON object. Max 10 KB serialized. |

### 13.2 Organization

| Field | Rule |
|-------|------|
| `category name` | 1–200 characters. Allowed: alphanumeric, hyphens, underscores, spaces. No leading/trailing whitespace. |
| `category path` | Slash-separated. Max depth: 10 levels. Each segment follows category name rules. |
| `tag name` | 1–100 characters. Lowercase, alphanumeric, hyphens. Auto-lowercased on input. |
| `tags per memory` | Max 50. |

### 13.3 Priority

| Field | Rule |
|-------|------|
| `priority` | Integer, 1–5. 1=trivial, 2=low, 3=normal, 4=high, 5=critical. Default: 3. |

### 13.4 Memory Type

| Field | Rule |
|-------|------|
| `type` | One of: `fact`, `decision`, `procedure`, `reference`, `observation`. Default: `fact`. |

### 13.5 Link Type

| Field | Rule |
|-------|------|
| `link_type` | One of: `related`, `supersedes`, `contradicts`, `extends`. |

### 13.6 Search Parameters

| Field | Rule |
|-------|------|
| `query` | 1–1,000 characters. |
| `limit` | 1–100. Default: 10. |
| `offset` | >= 0. Default: 0. |
| `date_from`, `date_to` | ISO 8601 format. `date_from` must be before `date_to`. |

### 13.7 IDs

| Field | Rule |
|-------|------|
| All `id` fields | UUID v4 format. Generated server-side on creation. |

---

## 14. Configuration

### 14.1 Configuration File

Location: `~/.memorybank/appsettings.json`

Created with defaults on first run if not present.

```json
{
  "database": {
    "path": "~/.memorybank/memorybank.db",
    "walMode": true,
    "busyTimeout": 5000
  },
  "backup": {
    "path": "~/.memorybank/backups/",
    "maxBackups": 10,
    "autoBackupEnabled": true,
    "autoBackupIntervalHours": 24
  },
  "embedding": {
    "modelPath": "~/.memorybank/models/all-MiniLM-L6-v2.onnx",
    "modelName": "all-MiniLM-L6-v2",
    "dimensions": 384,
    "maxTokensPerChunk": 400,
    "chunkOverlapTokens": 50
  },
  "search": {
    "defaultLimit": 10,
    "maxLimit": 100,
    "vectorWeight": 0.4,
    "keywordWeight": 0.35,
    "priorityWeight": 0.25,
    "pinBonus": 100,
    "recencyDecayPerDay": 0.5,
    "accessBoostFactor": 2
  },
  "validation": {
    "maxContentLength": 100000,
    "maxMetadataSize": 10240,
    "maxTagsPerMemory": 50,
    "maxCategoryDepth": 10,
    "maxTagLength": 100,
    "maxCategoryNameLength": 200
  },
  "memory": {
    "defaultPriority": 3,
    "defaultType": "fact",
    "duplicateThreshold": 0.90
  },
  "logging": {
    "level": "Information",
    "filePath": "~/.memorybank/logs/memorybank.log",
    "maxFileSizeMb": 50,
    "maxRetainedFiles": 5
  }
}
```

### 14.2 Configuration Precedence

1. `appsettings.json` (user-configured)
2. Built-in defaults (hardcoded, used if config file is missing or field is absent)

### 14.3 Environment Variable Override

Any config value can be overridden via environment variable:

```
MEMORYBANK__DATABASE__PATH=C:/custom/path/memorybank.db
MEMORYBANK__SEARCH__VECTORWEIGHT=0.5
```

Follows the `Microsoft.Extensions.Configuration` double-underscore convention.

---

## 15. Startup & Shutdown

### 15.1 First Run

On first launch, MemoryBank:

1. Creates `~/.memorybank/` directory if not exists
2. Creates `appsettings.json` with defaults if not exists
3. Creates SQLite database file and runs all schema migrations
4. Creates FTS5 virtual tables and triggers
5. Checks for ONNX embedding model at configured path
   - If not found: logs a warning, starts without embedding support. Vector search is disabled. Keyword search and structured filters still work.
   - Provides a clear error message: `"Embedding model not found at ~/.memorybank/models/all-MiniLM-L6-v2.onnx. Download it from [URL] and place it there. Vector search is disabled until the model is available."`
6. Creates `backups/` and `logs/` directories
7. Writes startup log entry
8. Server is ready — sends MCP initialization response

### 15.2 Subsequent Runs

1. Load configuration
2. Open existing database
3. Check schema version, run pending migrations if needed
4. Verify FTS index integrity (quick check, not full rebuild)
5. Load embedding model (or warn if missing)
6. Check for auto-backup schedule (create backup if overdue)
7. Server is ready

### 15.3 Graceful Shutdown

On receiving termination signal (parent process closes stdin, SIGTERM, etc.):

1. Finish any in-progress write operation (do not interrupt mid-transaction)
2. Flush WAL to main database file (`PRAGMA wal_checkpoint(TRUNCATE)`)
3. Close database connection
4. Write shutdown log entry
5. Exit with code 0

### 15.4 Crash Recovery

If MemoryBank was not shut down cleanly:

- SQLite WAL mode provides automatic crash recovery — no data loss for committed transactions
- On next startup, SQLite replays the WAL automatically
- `health_check` verifies integrity after recovery

---

## 16. Embedding Model Management

### 16.1 Model Location

Models are stored at: `~/.memorybank/models/`

### 16.2 Supported Models

| Model | File | Dimensions | Size |
|-------|------|-----------|------|
| `all-MiniLM-L6-v2` | `all-MiniLM-L6-v2.onnx` | 384 | ~80 MB |
| `bge-small-en-v1.5` | `bge-small-en-v1.5.onnx` | 384 | ~130 MB |
| `nomic-embed-text-v1.5` | `nomic-embed-text-v1.5.onnx` | 768 | ~270 MB |

### 16.3 Model Provisioning

MemoryBank does **not** auto-download models. The user must download and place the ONNX file manually. This is intentional:

- No network dependency at runtime
- User controls what runs on their machine
- No surprise bandwidth usage

Documentation and first-run message provide download instructions.

### 16.4 Model Switching

When changing the embedding model (different dimensions or different model):

1. Update `appsettings.json` with new model path, name, and dimensions
2. Restart MemoryBank
3. MemoryBank detects dimension mismatch with existing embeddings
4. Existing memories remain functional via keyword search
5. Call `reembed_all` tool to regenerate all embeddings with the new model
6. Progress is reported: `"Re-embedding: 1500/2847 memories complete"`

### 16.5 Degraded Mode (No Embedding Model)

If no ONNX model is available:

- MemoryBank starts normally
- `remember` stores content, chunks, FTS index — but skips embedding generation
- `recall` uses keyword search + structured filters only (no vector score)
- `health_check` reports `"embeddingStatus": "unavailable"`
- All other tools work normally

---

## 17. Installation & Distribution

### 17.1 Distribution Format

Distributed as a **.NET tool** (global dotnet tool):

```bash
dotnet tool install --global MemoryBank
```

This provides a `memorybank` command available system-wide.

### 17.2 MCP Client Configuration (after install)

```json
{
  "mcpServers": {
    "memorybank": {
      "command": "memorybank",
      "args": ["serve"]
    }
  }
}
```

### 17.3 CLI Commands

| Command | Purpose |
|---------|---------|
| `memorybank serve` | Start MCP server (stdio mode, used by AI clients) |
| `memorybank init` | Create config directory and default settings |
| `memorybank health` | Run health check and print status |
| `memorybank backup` | Create manual backup |
| `memorybank restore <path>` | Restore from backup |
| `memorybank reembed` | Regenerate all embeddings |
| `memorybank stats` | Print memory stats |
| `memorybank version` | Print version info |

### 17.4 Updating

```bash
dotnet tool update --global MemoryBank
```

Schema migrations run automatically on next startup.

### 17.5 Uninstalling

```bash
dotnet tool uninstall --global MemoryBank
```

Data directory (`~/.memorybank/`) is **not** deleted on uninstall. User must remove it manually if desired.

---

## 18. Backup System

### 18.1 Backup Location

Default: `~/.memorybank/backups/`

### 18.2 Backup Naming

```
memorybank_backup_2026-04-14T030000Z.db
```

ISO 8601 timestamp, file-safe format.

### 18.3 Automatic Backups

- Triggered on startup if last backup is older than `autoBackupIntervalHours` (default: 24h)
- Uses SQLite Online Backup API — safe to run while the database is in use
- Does not block read or write operations

### 18.4 Retention

- Max backups retained: configurable (default: 10)
- Oldest backup is deleted when limit is exceeded
- Never deletes the most recent backup

### 18.5 Restore Process

1. Verify backup file exists and is a valid SQLite database
2. Run `PRAGMA integrity_check` on backup file
3. Check schema version compatibility
4. Close current database connection
5. Replace current database file with backup
6. Reopen database, run any needed migrations
7. Rebuild FTS index
8. Log restore event

---

## 19. Concurrency

### 19.1 Single-Writer Model

- SQLite allows one writer at a time. WAL mode allows concurrent readers while writing.
- MemoryBank is designed for **single-instance** use. One AI client at a time.

### 19.2 Multiple AI Clients

If two AI clients try to launch MemoryBank against the same database simultaneously:

- SQLite `busy_timeout` (default: 5000ms) handles brief contention
- If the timeout expires, the second writer gets a `DATABASE_BUSY` error
- The error message instructs the user: `"Another MemoryBank instance may be using this database. Close the other AI client or configure a separate database path."`

### 19.3 Rapid Successive Calls

Within a single session, the AI may fire multiple tool calls quickly:

- All write operations use explicit transactions
- Reads are non-blocking (WAL mode)
- No request queuing needed — SQLite handles serialization internally

---

## 20. Testing Strategy

### 20.1 Unit Tests

- **Models**: Validation logic, priority calculation, score computation
- **Chunking**: Split logic, boundary detection, overlap handling, token counting
- **Search scoring**: Weight application, ranking order, edge cases (empty DB, no matches)
- **Configuration**: Default loading, override precedence, environment variables

### 20.2 Integration Tests

- **Storage round-trip**: Store → recall → verify content matches
- **FTS sync**: Store → keyword search → verify match
- **Embedding round-trip**: Store → vector search → verify semantic match
- **Revision lifecycle**: Store → update → update → get_revisions → restore → verify
- **Chunking lifecycle**: Store large content → recall by chunk → get full content
- **Link management**: Create → link → get_linked → unlink → verify
- **Category operations**: Create tree → move memory → delete empty → fail on non-empty
- **Bulk operations**: Import, bulk tag, bulk recategorize
- **Backup/restore**: Backup → modify data → restore → verify original state

### 20.3 Edge Case Tests

- Empty database: recall returns empty, count returns 0, exists returns false
- First memory stored: verify categories and tags auto-created
- Corrupt FTS index: health_check detects, recall degrades to non-FTS
- Missing embedding model: all tools work except vector search
- Max content size: verify rejection at boundary
- Concurrent writes: verify busy timeout behavior
- Disk full: verify transaction rollback and error message
- Unicode content: store and recall multilingual text
- Special characters in tags/categories: verify handling

### 20.4 Test Infrastructure

- Use in-memory SQLite for unit tests (`:memory:`)
- Use temporary file-based SQLite for integration tests
- Embed a small test ONNX model (~5 MB) for embedding tests
- Each test creates and destroys its own database — no shared state

### 20.5 Coverage Target

- Core logic (storage, search, chunking): > 90%
- MCP tool handlers: > 80%
- Configuration and startup: > 70%

---

## 21. Performance Targets

### 21.1 Tool Latency (measured at tool response, single-user)

| Operation | Target | Notes |
|-----------|--------|-------|
| `remember` (small, <500 tokens) | < 50ms | Excluding embedding generation |
| `remember` (small, with embedding) | < 100ms | Including ONNX inference |
| `remember` (large, chunked) | < 500ms | Proportional to chunk count |
| `recall` (hybrid search) | < 200ms | Up to 50,000 memories |
| `recall` (keyword only, no model) | < 100ms | FTS5 only |
| `get_memory` | < 10ms | Direct ID lookup |
| `update_memory` | < 100ms | Including revision + re-embedding |
| `forget` | < 50ms | Including cascade deletes |
| `list_categories` | < 20ms | Cached if unchanged |
| `health_check` | < 500ms | Includes integrity check |
| `backup` | < 5s | For databases up to 500 MB |

### 21.2 Startup Time

| Phase | Target |
|-------|--------|
| Cold start (no model loading) | < 500ms |
| Cold start (with model loading) | < 3s |
| Schema migration | < 2s per migration |

### 21.3 Resource Usage

| Resource | Target |
|----------|--------|
| Memory (idle) | < 50 MB |
| Memory (with ONNX model loaded) | < 200 MB |
| CPU (idle) | ~0% |
| CPU (during recall) | Brief spike, < 1s |

---

## 22. Logging

### 22.1 Log Format

Structured JSON logging via `Microsoft.Extensions.Logging`:

```json
{
  "timestamp": "2026-04-14T10:00:00.123Z",
  "level": "Information",
  "category": "MemoryBank.Server.Tools.RememberTool",
  "message": "Memory stored",
  "properties": {
    "memoryId": "550e8400-...",
    "category": "architecture/auth",
    "chunkCount": 1,
    "durationMs": 45
  }
}
```

### 22.2 Log Levels

| Level | What is logged |
|-------|---------------|
| `Debug` | Query details, scoring breakdowns, chunking decisions |
| `Information` | Tool calls (name, params summary, result count, duration) |
| `Warning` | Embedding model missing, FTS sync lag, approaching disk limits, duplicate detected |
| `Error` | Failed operations, DB errors, corrupt data detected |
| `Critical` | DB corruption, unrecoverable startup failure |

### 22.3 Log Output

- **Primary**: File at `~/.memorybank/logs/memorybank.log`
- Rolling file: max 50 MB per file, max 5 retained files
- **Secondary**: stderr (captured by MCP client for diagnostics)
- stdout is reserved exclusively for MCP protocol communication

### 22.4 Audit Log (in-database)

Separate from application logs. Stored in the `audit_log` table for queryable history:

- Every `remember`, `update_memory`, `forget`, `archive`, `restore_revision` writes an audit entry
- Every `recall` that returns results writes an audit entry (for access tracking)
- Audit log is never automatically purged

---

## 23. Database Migrations

### 23.1 Schema Versioning

A `schema_version` table tracks the current version:

```sql
CREATE TABLE schema_version (
    version     INTEGER NOT NULL,
    applied_at  TEXT NOT NULL,
    description TEXT
);
```

### 23.2 Migration Execution

- On startup, compare current schema version to latest known version
- Execute pending migrations in order, within a transaction
- Each migration is atomic — if it fails, the database remains at the previous version
- Log each migration applied

### 23.3 Migration Files

Stored as embedded resources in `MemoryBank.Core`:

```
Migrations/
├── V001_InitialSchema.cs
├── V002_AddMemoryType.cs
├── V003_AddAuditLog.cs
└── ...
```

Each migration contains:
- `Up()` — apply the migration
- `Down()` — reverse the migration (for development/testing only)
- Version number and description

### 23.4 Backward Compatibility

- Migrations only add columns, tables, or indexes. Never remove or rename without a multi-step migration.
- If a column is deprecated, it is kept but ignored until a major version bump.

---

## 24. Edge Cases

### 24.1 Empty Database

| Tool | Behavior |
|------|----------|
| `recall` | Returns `{ results: [], totalCount: 0, hasMore: false }` |
| `count` | Returns `0` |
| `exists` | Returns `false` |
| `list_categories` | Returns empty array |
| `list_tags` | Returns empty array |
| `memory_stats` | Returns all zeros |
| `health_check` | Returns `healthy` |
| `memorybank://index` resource | Returns empty categories, empty tags, totalMemories: 0 |

### 24.2 Category Auto-Creation

When `remember` is called with a category path that doesn't exist:

- `"projects/backend/auth"` — creates `projects`, `projects/backend`, and `projects/backend/auth` if any are missing
- Each intermediate category is created with no description

### 24.3 Tag Auto-Creation

When `remember` is called with tags that don't exist:

- Tags are auto-created. No separate creation step needed.
- Tag names are auto-lowercased and trimmed.

### 24.4 Orphan Cleanup

- After `forget` or `bulk_forget`: check if any tags or categories are now empty
- Do **not** auto-delete orphans. Report them via `health_check` under `"orphanedTags"` and `"orphanedCategories"` counts
- Provide explicit `cleanup_orphans` tool to delete them on demand

### 24.5 Disk Full

- SQLite transaction fails, rolls back automatically
- Return error: `"STORAGE_FULL: Unable to write to database. Free disk space and retry."`
- All existing data remains intact (transaction was rolled back)

### 24.6 Very Large Import

- Imports are processed in batches of 100 memories
- Each batch is a separate transaction
- If a batch fails, previous batches are committed, failed batch is rolled back
- Return partial result: `"Imported 1400 of 1500 memories. Batch 15 failed: VALIDATION_FAILED on 3 entries."`

### 24.7 Corrupt FTS Index

- Detected by `health_check` when FTS results don't match expected count
- Recovery: `DROP` and recreate FTS virtual table, re-populate from chunks table
- During rebuild, keyword search is unavailable; vector search and structured filters still work

### 24.8 Embedding Dimension Mismatch

- Detected on startup when configured model dimensions don't match stored embeddings
- MemoryBank starts normally. Existing vector search works with old embeddings.
- New memories get embeddings with the new dimensions — but these are incompatible with old ones
- `health_check` reports the mismatch and recommends `reembed_all`
- `reembed_all` replaces all embeddings with the new model

---

## 25. Future Considerations (V2+)

These are explicitly out of scope for V1 but worth noting:

- Multi-device sync
- Web UI for browsing and managing memories
- Scheduled knowledge digests ("weekly summary of what changed")
- Memory expiration with TTL
- Encryption at rest
- Multi-user / multi-tenant support
- SSE/HTTP transport for remote access
- Plugin system for custom memory processors
