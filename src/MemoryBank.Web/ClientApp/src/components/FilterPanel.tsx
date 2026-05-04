import { useEffect, useMemo, useState } from "react";
import {
  ChevronDown,
  ChevronRight,
  Eye,
  Filter,
  Info,
  Scaling,
  Spline,
  X,
} from "lucide-react";
import type { EdgeType, FilterOptions } from "../api/types";
import { fetchFilters } from "../api/client";
import { useStore } from "../store";
import { edgeColors } from "../lib/colors";
import { SIZING_STRATEGIES } from "../lib/nodeSizing";

const EDGE_TYPES: { value: EdgeType; label: string; hint: string }[] = [
  { value: "links", label: "Links", hint: "Explicit wiki-style links" },
  { value: "similarity", label: "Similarity", hint: "Semantic cosine over embeddings" },
  { value: "tags", label: "Tag overlap", hint: "Jaccard similarity on tags" },
  { value: "category", label: "Same category", hint: "Memories sharing a category" },
];

export function FilterPanel() {
  const [options, setOptions] = useState<FilterOptions | null>(null);

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
    sizingStrategies,
    toggleSizingStrategy,
    sidebarOpen,
  } = useStore();

  useEffect(() => {
    fetchFilters().then(setOptions).catch(console.error);
  }, []);

  const simActive = edgeTypes.includes("similarity");
  const tagsEdgeActive = edgeTypes.includes("tags");

  const activeFilterCount = useMemo(
    () =>
      categories.length +
      tags.length +
      types.length +
      (includeArchived ? 1 : 0),
    [categories, tags, types, includeArchived]
  );

  const clearAllFilters = () => {
    setCategories([]);
    setTags([]);
    setTypes([]);
    setIncludeArchived(false);
  };

  if (!sidebarOpen) return null;

  return (
    <aside className="absolute left-3 top-16 bottom-3 w-80 bg-panel backdrop-blur border border-panelBorder rounded-lg flex flex-col text-sm z-10 shadow-xl shadow-black/30">
      <div className="flex-1 overflow-y-auto panel-scroll px-3 py-3 space-y-3">
        {/* Show group — how the graph is rendered */}
        <Group icon={<Eye size={13} />} label="Show" defaultOpen>
          <Subsection
            icon={<Spline size={12} />}
            label="Edge types"
            badge={edgeTypes.length || undefined}
          >
            <div className="space-y-1">
              {EDGE_TYPES.map((et) => (
                <CheckboxRow
                  key={et.value}
                  checked={edgeTypes.includes(et.value)}
                  onChange={() => toggleEdgeType(et.value)}
                  swatch={edgeColors[et.value === "links" ? "link" : et.value]}
                  swatchKind="bar"
                  label={et.label}
                  hint={et.hint}
                />
              ))}
            </div>
            {simActive && !options?.embeddingsAvailable && (
              <p className="text-xs text-amber-400 mt-2 flex items-start gap-1.5">
                <Info size={11} className="mt-0.5 shrink-0" />
                <span>Similarity edges need embeddings (ONNX model not loaded).</span>
              </p>
            )}
            {simActive && (
              <NestedControls>
                <LogSlider
                  label={`Similarity threshold · ${simThreshold.toFixed(3)}`}
                  min={0.5}
                  max={0.95}
                  value={simThreshold}
                  onChange={setSimThreshold}
                />
                <div className="flex items-center justify-between text-xs text-slate-400 mt-2">
                  <span>Top-K neighbors</span>
                  <input
                    type="number"
                    min={1}
                    max={20}
                    value={simTopK}
                    onChange={(e) =>
                      setSimTopK(Math.max(1, Math.min(20, +e.target.value)))
                    }
                    className="w-14 bg-black/40 border border-panelBorder rounded px-2 py-0.5 text-right text-slate-200"
                  />
                </div>
              </NestedControls>
            )}
            {tagsEdgeActive && (
              <NestedControls>
                <LogSlider
                  label={`Tag Jaccard min · ${tagJaccardMin.toFixed(3)}`}
                  min={0.01}
                  max={1}
                  value={tagJaccardMin}
                  onChange={setTagJaccardMin}
                />
              </NestedControls>
            )}
          </Subsection>

          <Subsection
            icon={<Scaling size={12} />}
            label="Node size"
            badge={sizingStrategies.length || "uniform"}
          >
            <div className="space-y-1">
              {SIZING_STRATEGIES.map((s) => (
                <CheckboxRow
                  key={s.id}
                  checked={sizingStrategies.includes(s.id)}
                  onChange={() => toggleSizingStrategy(s.id)}
                  label={s.label}
                  hint={s.hint}
                />
              ))}
            </div>
            {sizingStrategies.length === 0 && (
              <p className="text-xs text-slate-500 mt-2 px-1">
                All nodes drawn at uniform size.
              </p>
            )}
          </Subsection>

        </Group>

        {/* Filter group — which memories are included */}
        <Group
          icon={<Filter size={13} />}
          label="Filter"
          badge={activeFilterCount || undefined}
          defaultOpen
          headerAction={
            activeFilterCount > 0 ? (
              <button
                onClick={clearAllFilters}
                className="text-[11px] text-slate-500 hover:text-slate-300 flex items-center gap-1"
                title="Clear all filters"
              >
                <X size={11} /> clear
              </button>
            ) : null
          }
        >
          <MultiSelect
            label="Categories"
            options={
              options?.categories.map((c) => ({
                value: c.path,
                label: c.path,
                count: c.memoryCount,
              })) ?? []
            }
            selected={categories}
            onChange={setCategories}
          />
          <MultiSelect
            label="Tags"
            options={
              options?.tags.map((t) => ({
                value: t.name,
                label: t.name,
                count: t.count,
              })) ?? []
            }
            selected={tags}
            onChange={setTags}
          />
          <MultiSelect
            label="Types"
            options={options?.types.map((t) => ({ value: t, label: t })) ?? []}
            selected={types}
            onChange={setTypes}
          />
          <label className="flex items-center gap-2 cursor-pointer px-2 py-1.5 hover:bg-white/5 rounded">
            <input
              type="checkbox"
              checked={includeArchived}
              onChange={(e) => setIncludeArchived(e.target.checked)}
              className="accent-sky-500"
            />
            <span className="text-slate-300 text-xs">Include archived</span>
          </label>
        </Group>
      </div>
    </aside>
  );
}

// ── Layout primitives ─────────────────────────────────────────────────

function Group({
  icon,
  label,
  badge,
  defaultOpen = false,
  headerAction,
  children,
}: {
  icon: React.ReactNode;
  label: string;
  badge?: number | string;
  defaultOpen?: boolean;
  headerAction?: React.ReactNode;
  children: React.ReactNode;
}) {
  const [open, setOpen] = useState(defaultOpen);
  return (
    <section className="border border-panelBorder/70 rounded-lg overflow-hidden bg-black/20">
      <header className="flex items-center justify-between px-3 py-2 bg-white/[0.02]">
        <button
          onClick={() => setOpen(!open)}
          className="flex items-center gap-2 flex-1 text-left text-slate-200 hover:text-white"
        >
          {open ? <ChevronDown size={13} /> : <ChevronRight size={13} />}
          <span className="text-slate-400">{icon}</span>
          <span className="uppercase tracking-wider text-xs font-medium">{label}</span>
          {badge != null && (
            <span className="ml-1 px-1.5 py-0.5 rounded text-[10px] bg-sky-500/20 text-sky-300 font-medium">
              {badge}
            </span>
          )}
        </button>
        {headerAction}
      </header>
      {open && <div className="px-3 py-3 space-y-4">{children}</div>}
    </section>
  );
}

function Subsection({
  icon,
  label,
  badge,
  children,
}: {
  icon?: React.ReactNode;
  label: string;
  badge?: number | string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <div className="flex items-center gap-1.5 mb-2 text-slate-400">
        {icon}
        <span className="text-[11px] uppercase tracking-wider font-medium">{label}</span>
        {badge != null && (
          <span className="ml-1 text-[10px] text-slate-500">({badge})</span>
        )}
      </div>
      {children}
    </div>
  );
}

function NestedControls({ children }: { children: React.ReactNode }) {
  return (
    <div className="mt-2 ml-2 pl-3 border-l border-panelBorder/70 space-y-1">
      {children}
    </div>
  );
}

function CheckboxRow({
  checked,
  onChange,
  label,
  hint,
  swatch,
  swatchKind = "dot",
}: {
  checked: boolean;
  onChange: () => void;
  label: string;
  hint?: string;
  swatch?: string;
  swatchKind?: "dot" | "bar";
}) {
  return (
    <label className="flex items-start gap-2 cursor-pointer hover:bg-white/5 px-2 py-1 rounded">
      <input
        type="checkbox"
        checked={checked}
        onChange={onChange}
        className="mt-0.5 accent-sky-500"
      />
      <div className="flex-1">
        <div className="flex items-center gap-2 text-slate-200 text-xs">
          {swatch && swatchKind === "dot" && (
            <span
              className="w-2.5 h-2.5 rounded-full shrink-0"
              style={{ background: swatch }}
            />
          )}
          {swatch && swatchKind === "bar" && (
            <span
              className="w-3 h-0.5 rounded shrink-0"
              style={{ background: swatch }}
            />
          )}
          <span>{label}</span>
        </div>
        {hint && <div className="text-[11px] text-slate-500 mt-0.5">{hint}</div>}
      </div>
    </label>
  );
}

/**
 * Logarithmic slider — the internal position is linear 0..1, but values are mapped
 * exponentially between `min` and `max`. This dedicates half the slider travel to
 * the first decade, giving fine-grained control at low values.
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
      <div className="text-[11px] text-slate-400 mb-1">{label}</div>
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
      <div>
        <div className="text-[11px] uppercase tracking-wider text-slate-400 mb-1">
          {label}
        </div>
        <p className="text-xs text-slate-500 px-2">None available</p>
      </div>
    );
  }

  const toggle = (value: string) =>
    onChange(
      selected.includes(value)
        ? selected.filter((v) => v !== value)
        : [...selected, value]
    );

  const filtered = options.filter((o) =>
    o.label.toLowerCase().includes(filter.toLowerCase())
  );
  const displayed = expanded ? filtered : filtered.slice(0, 6);

  return (
    <div>
      <div className="flex items-center justify-between mb-1.5">
        <span className="text-[11px] uppercase tracking-wider text-slate-400">
          {label}{" "}
          {selected.length > 0 && (
            <span className="text-sky-400 normal-case">({selected.length})</span>
          )}
        </span>
        {selected.length > 0 && (
          <button
            onClick={() => onChange([])}
            className="text-[10px] text-slate-500 hover:text-slate-300"
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
          className="w-full bg-black/40 border border-panelBorder rounded px-2 py-1 text-xs mb-1.5 focus:outline-none focus:border-sky-500/60"
        />
      )}
      <div className="space-y-0.5 max-h-56 overflow-y-auto panel-scroll">
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
            <span className="flex-1 truncate text-slate-200">{opt.label}</span>
            {opt.count != null && (
              <span className="text-slate-500 tabular-nums">{opt.count}</span>
            )}
          </label>
        ))}
      </div>
      {filtered.length > 6 && (
        <button
          onClick={() => setExpanded((e) => !e)}
          className="text-[11px] text-slate-500 hover:text-slate-300 mt-1"
        >
          {expanded ? "Show less" : `Show ${filtered.length - 6} more`}
        </button>
      )}
    </div>
  );
}
