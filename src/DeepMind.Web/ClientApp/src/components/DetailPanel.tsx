import { useEffect, useState } from "react";
import {
  Archive,
  Clock,
  Hash,
  History,
  Link as LinkIcon,
  Pin,
  Puzzle,
  Tag,
  X,
} from "lucide-react";
import type { MemoryDetail } from "../api/types";
import { fetchMemoryDetail } from "../api/client";
import { useStore } from "../store";

export function DetailPanel() {
  const selectedNodeId = useStore((s) => s.selectedNodeId);
  const setSelectedNode = useStore((s) => s.setSelectedNode);
  const [detail, setDetail] = useState<MemoryDetail | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!selectedNodeId) {
      setDetail(null);
      setError(null);
      return;
    }
    setLoading(true);
    fetchMemoryDetail(selectedNodeId)
      .then((d) => {
        setDetail(d);
        setError(null);
      })
      .catch((err) => setError(String(err)))
      .finally(() => setLoading(false));
  }, [selectedNodeId]);

  if (!selectedNodeId) return null;

  return (
    <aside className="absolute right-3 top-3 bottom-3 w-96 bg-panel backdrop-blur border border-panelBorder rounded-lg flex flex-col text-sm z-10">
      <header className="px-4 py-3 border-b border-panelBorder flex items-start justify-between gap-2">
        <div className="flex-1 min-w-0">
          {loading && <div className="text-slate-500">Loading…</div>}
          {error && <div className="text-red-400">{error}</div>}
          {detail && (
            <>
              <div className="text-xs uppercase tracking-wider text-slate-500 flex items-center gap-1.5">
                <span className="text-sky-400">{detail.type}</span>
                {detail.pinned && <Pin size={11} className="text-amber-400" />}
                {detail.archived && <Archive size={11} className="text-slate-400" />}
              </div>
              <h2 className="font-semibold text-slate-100 mt-1 line-clamp-2">
                {detail.summary || truncate(detail.content, 80)}
              </h2>
            </>
          )}
        </div>
        <button
          onClick={() => setSelectedNode(null)}
          className="p-1 rounded hover:bg-white/5"
          title="Close"
        >
          <X size={14} />
        </button>
      </header>

      {detail && (
        <div className="flex-1 overflow-y-auto panel-scroll px-4 py-3 space-y-5">
          <section>
            <div className="text-xs text-slate-400 whitespace-pre-wrap leading-relaxed">
              {detail.content}
            </div>
          </section>

          <section className="grid grid-cols-2 gap-2 text-xs">
            <Meta icon={<Hash size={11} />} label="Priority" value={detail.priority.toString()} />
            <Meta icon={<Clock size={11} />} label="Created" value={formatDate(detail.createdAt)} />
            <Meta icon={<Clock size={11} />} label="Updated" value={formatDate(detail.updatedAt)} />
            <Meta icon={<History size={11} />} label="Revisions" value={detail.revisionNumber.toString()} />
            <Meta icon={<History size={11} />} label="Access count" value={detail.accessCount.toString()} />
            {detail.categoryPath && (
              <Meta icon={<Puzzle size={11} />} label="Category" value={detail.categoryPath} />
            )}
          </section>

          {detail.tags.length > 0 && (
            <section>
              <SectionHeader icon={<Tag size={12} />} label="Tags" />
              <div className="flex flex-wrap gap-1.5 mt-2">
                {detail.tags.map((t) => (
                  <span
                    key={t}
                    className="text-xs bg-orange-500/10 text-orange-300 border border-orange-500/30 rounded px-2 py-0.5"
                  >
                    #{t}
                  </span>
                ))}
              </div>
            </section>
          )}

          {detail.linkedMemories.length > 0 && (
            <section>
              <SectionHeader icon={<LinkIcon size={12} />} label="Linked memories" />
              <ul className="mt-2 space-y-1">
                {detail.linkedMemories.map((l) => (
                  <li key={l.id + l.direction}>
                    <button
                      onClick={() => setSelectedNode(l.id)}
                      className="text-left text-xs text-sky-300 hover:text-sky-200 hover:bg-white/5 rounded px-2 py-1 w-full truncate"
                    >
                      → {l.label}
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          )}

          {detail.chunks.length > 1 && (
            <Collapsible label={`Chunks (${detail.chunks.length})`}>
              <ul className="mt-1 space-y-2">
                {detail.chunks.map((c) => (
                  <li key={c.id} className="text-xs border-l-2 border-slate-700 pl-2">
                    <div className="text-slate-500">#{c.index}{c.summary ? ` · ${c.summary}` : ""}</div>
                    <div className="text-slate-400">{c.contentPreview}</div>
                  </li>
                ))}
              </ul>
            </Collapsible>
          )}

          {detail.revisions.length > 0 && (
            <Collapsible label={`Revisions (${detail.revisions.length})`}>
              <ul className="mt-1 space-y-2">
                {detail.revisions.map((r) => (
                  <li key={r.number} className="text-xs border-l-2 border-slate-700 pl-2">
                    <div className="text-slate-500">
                      v{r.number} · {formatDate(r.createdAt)}
                      {r.reason && ` · ${r.reason}`}
                    </div>
                    <div className="text-slate-400">{r.contentPreview}</div>
                  </li>
                ))}
              </ul>
            </Collapsible>
          )}
        </div>
      )}
    </aside>
  );
}

function SectionHeader({ icon, label }: { icon: React.ReactNode; label: string }) {
  return (
    <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-slate-400">
      {icon}
      <span>{label}</span>
    </div>
  );
}

function Meta({
  icon,
  label,
  value,
}: {
  icon: React.ReactNode;
  label: string;
  value: string;
}) {
  return (
    <div className="bg-black/30 rounded px-2 py-1.5">
      <div className="text-slate-500 flex items-center gap-1.5">
        {icon}
        <span>{label}</span>
      </div>
      <div className="text-slate-200 truncate">{value}</div>
    </div>
  );
}

function Collapsible({ label, children }: { label: string; children: React.ReactNode }) {
  const [open, setOpen] = useState(false);
  return (
    <section>
      <button
        onClick={() => setOpen(!open)}
        className="text-xs uppercase tracking-wider text-slate-400 hover:text-slate-200"
      >
        {open ? "▾" : "▸"} {label}
      </button>
      {open && <div>{children}</div>}
    </section>
  );
}

function truncate(text: string, max: number): string {
  return text.length <= max ? text : text.substring(0, max) + "…";
}

function formatDate(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleDateString() + " " + d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}
