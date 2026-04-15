import { useEffect, useState } from "react";
import { Filter, Loader2, RefreshCw, Search, Sparkles } from "lucide-react";
import type { EdgeType, FilterOptions } from "../api/types";
import { fetchFilters, searchMemories } from "../api/client";
import { useStore } from "../store";
import { edgeColors } from "../lib/colors";

interface Props {
  onRefresh: () => void;
  loading: boolean;
  shown: number;
  total: number;
}

const EDGE_TYPES: { value: EdgeType; label: string; hint: string }[] = [
  { value: "links", label: "Links", hint: "Explicit wiki-style links" },
  { value: "similarity", label: "Similarity", hint: "Semantic cosine over embeddings" },
  { value: "tags", label: "Tag overlap", hint: "Jaccard similarity on tags" },
  { value: "category", label: "Same category", hint: "Memories sharing a category" },
];

export function FilterPanel({ onRefresh, loading, shown, total }: Props) {
  const [options, setOptions] = useState<FilterOptions | null>(null);
  const [searching, setSearching] = useState(false);

  const {
    edgeTypes,
    toggleEdgeType,
    categories,
    setCategories,
    tags,
    setTags,
    types,
    setTypes,
    includeArchived,
    setIncludeArchived,
    simThreshold,
    setSimThreshold,
    simTopK,
    setSimTopK,
    tagJaccardMin,
    setTagJaccardMin,
    searchQuery,
    setSearchQuery,
    setSearchScores,
  } = useStore();

  useEffect(() => {
    fetchFilters().then(setOptions).catch(console.error);
  }, []);

  // Live search → dims non-matching nodes and scales matches by score.
  // Short 120ms debounce so typing feels instant without hammering the API.
  useEffect(() => {
    const q = searchQuery.trim();
    if (!q) {
      setSearchScores(null);
      setSearching(false);
      return;
    }

    const controller = new AbortController();
    setSearching(true);

    const handle = setTimeout(async () => {
      try {
        const hits = await searchMemories(q, controller.signal);
        if (controller.signal.aborted) return;
        if (hits.length === 0) {
          setSearchScores(new Map());
          return;
        }
        // Normalize matchScore across the result set so sizing is RELATIVE:
        // best match in this search = 1.0, worst match = 0.0. This gives a clear
        // visual ranking regardless of the absolute score range.
        // Single-hit case: assign 1.0 directly (min==max would cause divide-by-zero).
        const scores = new Map<string, number>();
        if (hits.length === 1) {
          scores.set(hits[0].id, 1);
        } else {
          const maxScore = Math.max(...hits.map((h) => h.matchScore));
          const minScore = Math.min(...hits.map((h) => h.matchScore));
          const range = Math.max(0.0001, maxScore - minScore);
          for (const hit of hits) {
            scores.set(hit.id, (hit.matchScore - minScore) / range);
          }
        }
        setSearchScores(scores);
      } catch (err) {
        if (!(err instanceof DOMException && err.name === "AbortError")) {
          console.error(err);
        }
      } finally {
        if (!controller.signal.aborted) setSearching(false);
      }
    }, 120);

    return () => {
      clearTimeout(handle);
      controller.abort();
    };
  }, [searchQuery, setSearchScores]);

  const simActive = edgeTypes.includes("similarity");
  const tagsEdgeActive = edgeTypes.includes("tags");

  return (
    <aside className="absolute left-3 top-3 bottom-3 w-80 bg-panel backdrop-blur border border-panelBorder rounded-lg flex flex-col text-sm z-10">
      <header className="px-4 py-3 border-b border-panelBorder flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Filter size={16} className="text-slate-400" />
          <span className="font-semibold">DeepMind Graph</span>
        </div>
        <button
          onClick={onRefresh}
          disabled={loading}
          className="p-1.5 rounded hover:bg-white/5 disabled:opacity-50"
          title="Refresh graph"
        >
          <RefreshCw size={14} className={loading ? "animate-spin" : ""} />
        </button>
      </header>

      <div className="flex-1 overflow-y-auto panel-scroll px-4 py-3 space-y-5">
        {/* Search */}
        <section>
          <div className="flex items-center gap-2 text-slate-300 mb-2">
            <Search size={14} />
            <span className="text-xs uppercase tracking-wider">Search</span>
          </div>
          <div className="relative">
            <input
              type="text"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Highlight matching memories…"
              className="w-full bg-black/40 border border-panelBorder rounded pl-3 pr-8 py-2 text-sm focus:outline-none focus:border-sky-500"
            />
            {searching && (
              <Loader2
                size={14}
                className="absolute right-2.5 top-1/2 -translate-y-1/2 text-sky-400 animate-spin"
                aria-label="Searching"
              />
            )}
          </div>
          {searchQuery && !searching && (
            <p className="text-xs text-slate-500 mt-1">
              Uses hybrid semantic + keyword search
            </p>
          )}
        </section>

        {/* Edge types */}
        <section>
          <div className="flex items-center gap-2 text-slate-300 mb-2">
            <Sparkles size={14} />
            <span className="text-xs uppercase tracking-wider">Edge types</span>
          </div>
          <div className="space-y-1.5">
            {EDGE_TYPES.map((et) => (
              <label
                key={et.value}
                className="flex items-start gap-2 cursor-pointer hover:bg-white/5 px-2 py-1.5 rounded"
              >
                <input
                  type="checkbox"
                  checked={edgeTypes.includes(et.value)}
                  onChange={() => toggleEdgeType(et.value)}
                  className="mt-0.5 accent-sky-500"
                />
                <div className="flex-1">
                  <div className="flex items-center gap-2">
                    <span
                      className="w-2.5 h-2.5 rounded-full"
                      style={{ background: edgeColors[et.value === "links" ? "link" : et.value] }}
                    />
                    <span>{et.label}</span>
                  </div>
                  <div className="text-xs text-slate-500">{et.hint}</div>
                </div>
              </label>
            ))}
          </div>
          {simActive && !options?.embeddingsAvailable && (
            <p className="text-xs text-amber-400 mt-2">
              Similarity edges require embeddings (ONNX model not loaded).
            </p>
          )}
        </section>

        {/* Thresholds */}
        {simActive && (
          <section>
            <div className="text-xs uppercase tracking-wider text-slate-300 mb-2">
              Similarity
            </div>
            <LogSlider
              label={`Threshold ${simThreshold.toFixed(3)}`}
              min={0.5}
              max={0.95}
              value={simThreshold}
              onChange={setSimThreshold}
            />
            <label className="flex items-center justify-between text-xs text-slate-400 mt-2">
              <span>Top-K neighbors</span>
              <input
                type="number"
                min={1}
                max={20}
                value={simTopK}
                onChange={(e) => setSimTopK(Math.max(1, Math.min(20, +e.target.value)))}
                className="w-16 bg-black/40 border border-panelBorder rounded px-2 py-1 text-right"
              />
            </label>
          </section>
        )}

        {tagsEdgeActive && (
          <section>
            <div className="text-xs uppercase tracking-wider text-slate-300 mb-2">
              Tag overlap
            </div>
            <LogSlider
              label={`Jaccard min ${tagJaccardMin.toFixed(3)}`}
              min={0.01}
              max={1}
              value={tagJaccardMin}
              onChange={setTagJaccardMin}
            />
          </section>
        )}

        {/* Filters */}
        <MultiSelect
          label="Categories"
          options={options?.categories.map((c) => ({ value: c.path, label: c.path, count: c.memoryCount })) ?? []}
          selected={categories}
          onChange={setCategories}
        />
        <MultiSelect
          label="Tags"
          options={options?.tags.map((t) => ({ value: t.name, label: t.name, count: t.count })) ?? []}
          selected={tags}
          onChange={setTags}
        />
        <MultiSelect
          label="Types"
          options={options?.types.map((t) => ({ value: t, label: t })) ?? []}
          selected={types}
          onChange={setTypes}
        />

        <section>
          <label className="flex items-center gap-2 cursor-pointer">
            <input
              type="checkbox"
              checked={includeArchived}
              onChange={(e) => setIncludeArchived(e.target.checked)}
              className="accent-sky-500"
            />
            <span className="text-slate-300">Include archived</span>
          </label>
        </section>
      </div>

      <footer className="px-4 py-2.5 border-t border-panelBorder text-xs text-slate-400 flex items-center justify-between">
        <span>
          Showing <span className="text-slate-200">{shown}</span> of{" "}
          <span className="text-slate-200">{total}</span>
        </span>
        {shown < total && <span className="text-amber-400">truncated</span>}
      </footer>
    </aside>
  );
}

// ── Sub-components ─────────────────────────────────────────────────────

/**
 * Logarithmic slider — the internal position is linear 0..1, but values are mapped
 * exponentially between `min` and `max`. This dedicates half the slider travel to
 * the first decade, giving fine-grained control at low values.
 *
 * Example with min=0.01, max=1.0:
 *   position 0.00  →  value 0.01
 *   position 0.50  →  value 0.10
 *   position 1.00  →  value 1.00
 */
function LogSlider({
  label,
  min,
  max,
  value,
  onChange,
}: {
  label: string;
  min: number;
  max: number;
  value: number;
  onChange: (v: number) => void;
}) {
  const logMin = Math.log(min);
  const logMax = Math.log(max);
  const clamped = Math.max(min, Math.min(max, value));
  const position = (Math.log(clamped) - logMin) / (logMax - logMin);

  const handleChange = (pos: number) => {
    const v = Math.exp(logMin + pos * (logMax - logMin));
    onChange(v);
  };

  return (
    <div>
      <div className="flex items-center justify-between text-xs text-slate-400 mb-1">
        <span>{label}</span>
      </div>
      <input
        type="range"
        min={0}
        max={1}
        step={0.002}
        value={position}
        onChange={(e) => handleChange(+e.target.value)}
        className="w-full accent-sky-500"
      />
    </div>
  );
}

interface Option {
  value: string;
  label: string;
  count?: number;
}

function MultiSelect({
  label,
  options,
  selected,
  onChange,
}: {
  label: string;
  options: Option[];
  selected: string[];
  onChange: (v: string[]) => void;
}) {
  const [expanded, setExpanded] = useState(false);
  const [filter, setFilter] = useState("");

  if (options.length === 0) {
    return (
      <section>
        <div className="text-xs uppercase tracking-wider text-slate-300 mb-2">{label}</div>
        <p className="text-xs text-slate-500">None available</p>
      </section>
    );
  }

  const toggle = (value: string) =>
    onChange(selected.includes(value) ? selected.filter((v) => v !== value) : [...selected, value]);

  const filtered = options.filter((o) =>
    o.label.toLowerCase().includes(filter.toLowerCase())
  );
  const displayed = expanded ? filtered : filtered.slice(0, 6);

  return (
    <section>
      <div className="flex items-center justify-between mb-2">
        <span className="text-xs uppercase tracking-wider text-slate-300">
          {label}{" "}
          {selected.length > 0 && (
            <span className="text-sky-400 normal-case">({selected.length})</span>
          )}
        </span>
        {selected.length > 0 && (
          <button
            onClick={() => onChange([])}
            className="text-xs text-slate-500 hover:text-slate-300"
          >
            clear
          </button>
        )}
      </div>
      {options.length > 8 && (
        <input
          type="text"
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          placeholder={`Filter ${label.toLowerCase()}…`}
          className="w-full bg-black/40 border border-panelBorder rounded px-2 py-1 text-xs mb-2 focus:outline-none focus:border-sky-500"
        />
      )}
      <div className="space-y-0.5 max-h-60 overflow-y-auto panel-scroll">
        {displayed.map((opt) => (
          <label
            key={opt.value}
            className="flex items-center gap-2 text-xs cursor-pointer hover:bg-white/5 px-2 py-1 rounded"
          >
            <input
              type="checkbox"
              checked={selected.includes(opt.value)}
              onChange={() => toggle(opt.value)}
              className="accent-sky-500"
            />
            <span className="flex-1 truncate">{opt.label}</span>
            {opt.count != null && <span className="text-slate-500">{opt.count}</span>}
          </label>
        ))}
      </div>
      {filtered.length > 6 && (
        <button
          onClick={() => setExpanded((e) => !e)}
          className="text-xs text-slate-500 hover:text-slate-300 mt-1"
        >
          {expanded ? "Show less" : `Show ${filtered.length - 6} more`}
        </button>
      )}
    </section>
  );
}
