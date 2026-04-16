namespace MemoryBank.Core.Storage;

public record Migration(int Version, string Description, string Sql);

public static class Migrations
{
    public static readonly Migration[] All =
    [
        new(1, "Initial schema", """
            CREATE TABLE categories (
                id          TEXT PRIMARY KEY,
                name        TEXT NOT NULL,
                parent_id   TEXT REFERENCES categories(id),
                description TEXT
            );

            CREATE TABLE memories (
                id              TEXT PRIMARY KEY,
                content         TEXT NOT NULL,
                summary         TEXT,
                category_id     TEXT REFERENCES categories(id),
                type            TEXT NOT NULL DEFAULT 'fact'
                                CHECK(type IN ('fact','decision','procedure','reference','observation')),
                priority        INTEGER NOT NULL DEFAULT 3,
                is_pinned       INTEGER NOT NULL DEFAULT 0,
                is_archived     INTEGER NOT NULL DEFAULT 0,
                access_count    INTEGER NOT NULL DEFAULT 0,
                revision_number INTEGER NOT NULL DEFAULT 1,
                token_count     INTEGER,
                valid_from      TEXT,
                valid_until     TEXT,
                source          TEXT,
                metadata        TEXT,
                created_at      TEXT NOT NULL,
                updated_at      TEXT NOT NULL,
                last_accessed   TEXT
            );

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

            CREATE TABLE chunks (
                id          TEXT PRIMARY KEY,
                memory_id   TEXT NOT NULL REFERENCES memories(id) ON DELETE CASCADE,
                chunk_index INTEGER NOT NULL,
                content     TEXT NOT NULL,
                summary     TEXT,
                token_count INTEGER,
                created_at  TEXT NOT NULL,
                UNIQUE(memory_id, chunk_index)
            );

            CREATE TABLE embeddings (
                id          TEXT PRIMARY KEY,
                chunk_id    TEXT REFERENCES chunks(id) ON DELETE CASCADE,
                memory_id   TEXT NOT NULL REFERENCES memories(id) ON DELETE CASCADE,
                embedding   BLOB NOT NULL,
                model       TEXT NOT NULL,
                created_at  TEXT NOT NULL
            );

            CREATE TABLE tags (
                id   TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE
            );

            CREATE TABLE memory_tags (
                memory_id TEXT REFERENCES memories(id) ON DELETE CASCADE,
                tag_id    TEXT REFERENCES tags(id),
                PRIMARY KEY (memory_id, tag_id)
            );

            CREATE TABLE memory_links (
                source_id TEXT REFERENCES memories(id) ON DELETE CASCADE,
                target_id TEXT REFERENCES memories(id) ON DELETE CASCADE,
                link_type TEXT NOT NULL,
                PRIMARY KEY (source_id, target_id)
            );

            CREATE TABLE audit_log (
                id         TEXT PRIMARY KEY,
                memory_id  TEXT,
                action     TEXT NOT NULL,
                details    TEXT,
                created_at TEXT NOT NULL
            );

            -- FTS5 for chunk-level full-text search
            CREATE VIRTUAL TABLE chunks_fts USING fts5(
                content,
                summary,
                content='chunks',
                content_rowid='rowid',
                tokenize='porter unicode61'
            );

            -- Triggers to keep FTS in sync
            CREATE TRIGGER chunks_ai AFTER INSERT ON chunks BEGIN
                INSERT INTO chunks_fts(rowid, content, summary)
                VALUES (new.rowid, new.content, new.summary);
            END;

            CREATE TRIGGER chunks_ad AFTER DELETE ON chunks BEGIN
                INSERT INTO chunks_fts(chunks_fts, rowid, content, summary)
                VALUES ('delete', old.rowid, old.content, old.summary);
            END;

            CREATE TRIGGER chunks_au AFTER UPDATE ON chunks BEGIN
                INSERT INTO chunks_fts(chunks_fts, rowid, content, summary)
                VALUES ('delete', old.rowid, old.content, old.summary);
                INSERT INTO chunks_fts(rowid, content, summary)
                VALUES (new.rowid, new.content, new.summary);
            END;

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
            CREATE INDEX idx_categories_parent ON categories(parent_id);
            """),

        new(2, "Add keywords column to chunks and rebuild FTS5 with column weights", """
            -- Add keywords column
            ALTER TABLE chunks ADD COLUMN keywords TEXT;

            -- Drop old FTS5 table and triggers
            DROP TRIGGER IF EXISTS chunks_ai;
            DROP TRIGGER IF EXISTS chunks_ad;
            DROP TRIGGER IF EXISTS chunks_au;
            DROP TABLE IF EXISTS chunks_fts;

            -- Recreate FTS5 with keywords column and column weights via rank
            CREATE VIRTUAL TABLE chunks_fts USING fts5(
                content,
                summary,
                keywords,
                content='chunks',
                content_rowid='rowid',
                tokenize='porter unicode61'
            );

            -- Re-populate FTS from existing data
            INSERT INTO chunks_fts(rowid, content, summary, keywords)
            SELECT rowid, content, summary, keywords FROM chunks;

            -- Recreate triggers with keywords column
            CREATE TRIGGER chunks_ai AFTER INSERT ON chunks BEGIN
                INSERT INTO chunks_fts(rowid, content, summary, keywords)
                VALUES (new.rowid, new.content, new.summary, new.keywords);
            END;

            CREATE TRIGGER chunks_ad AFTER DELETE ON chunks BEGIN
                INSERT INTO chunks_fts(chunks_fts, rowid, content, summary, keywords)
                VALUES ('delete', old.rowid, old.content, old.summary, old.keywords);
            END;

            CREATE TRIGGER chunks_au AFTER UPDATE ON chunks BEGIN
                INSERT INTO chunks_fts(chunks_fts, rowid, content, summary, keywords)
                VALUES ('delete', old.rowid, old.content, old.summary, old.keywords);
                INSERT INTO chunks_fts(rowid, content, summary, keywords)
                VALUES (new.rowid, new.content, new.summary, new.keywords);
            END;
            """)
    ];
}
