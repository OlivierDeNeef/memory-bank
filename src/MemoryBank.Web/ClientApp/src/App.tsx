import { useCallback, useEffect, useRef, useState } from "react";
import { Brain, Loader2, PanelLeft, RefreshCw, Search } from "lucide-react";
import { fetchGraph, searchMemories } from "./api/client";
import type { GraphResponse } from "./api/types";
import { Graph2D } from "./components/Graph2D";
import { Graph3D } from "./components/Graph3D";
import { FilterPanel } from "./components/FilterPanel";
import { DetailPanel } from "./components/DetailPanel";
import { Legend } from "./components/Legend";
import { useStore } from "./store";

export default function App() {
  const [graph, setGraph] = useState<GraphResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [searching, setSearching] = useState(false);

  const edgeTypes = useStore((s) => s.edgeTypes);
  const categories = useStore((s) => s.categories);
  const tags = useStore((s) => s.tags);
  const types = useStore((s) => s.types);
  const includeArchived = useStore((s) => s.includeArchived);
  const simThreshold = useStore((s) => s.simThreshold);
  const simTopK = useStore((s) => s.simTopK);
  const tagJaccardMin = useStore((s) => s.tagJaccardMin);
  const limit = useStore((s) => s.limit);
  const viewMode = useStore((s) => s.viewMode);
  const setViewMode = useStore((s) => s.setViewMode);
  const sidebarOpen = useStore((s) => s.sidebarOpen);
  const setSidebarOpen = useStore((s) => s.setSidebarOpen);
  const selectedNodeId = useStore((s) => s.selectedNodeId);
  const hoveredNodeId = useStore((s) => s.hoveredNodeId);
  const searchQuery = useStore((s) => s.searchQuery);
  const setSearchQuery = useStore((s) => s.setSearchQuery);
  const setSearchScores = useStore((s) => s.setSearchScores);
  const bumpLayoutResetToken = useStore((s) => s.bumpLayoutResetToken);
  const buildQuery = useStore((s) => s.buildGraphQuery);

  // Hover wins over selection — matches the focus concept inside the graph components.
  const focusId = hoveredNodeId ?? selectedNodeId;
  const focusLabel = focusId
    ? graph?.nodes.find((n) => n.id === focusId)?.label ?? null
    : null;

  // Cancel in-flight requests when filters change rapidly so only the latest response applies.
  const abortRef = useRef<AbortController | null>(null);

  const refresh = useCallback(async () => {
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;

    setLoading(true);
    setError(null);
    try {
      const response = await fetchGraph(buildQuery(), controller.signal);
      if (!controller.signal.aborted) setGraph(response);
    } catch (err) {
      if (!(err instanceof DOMException && err.name === "AbortError")) {
        setError(String(err));
      }
    } finally {
      if (!controller.signal.aborted) setLoading(false);
    }
  }, [buildQuery]);

  // Manual refresh: also discard the cached layout so nodes return to their
  // initial state instead of drifting further with each click (residual
  // velocities on cached nodes get amplified by the alpha=1 re-heat).
  const handleManualRefresh = useCallback(() => {
    bumpLayoutResetToken();
    refresh();
  }, [bumpLayoutResetToken, refresh]);

  // Auto-refresh on any filter change. Debounced so dragging sliders doesn't spam the API.
  const isFirstRender = useRef(true);
  useEffect(() => {
    const delay = isFirstRender.current ? 0 : 200;
    isFirstRender.current = false;
    const handle = setTimeout(refresh, delay);
    return () => clearTimeout(handle);
  }, [
    refresh,
    edgeTypes,
    categories,
    tags,
    types,
    includeArchived,
    simThreshold,
    simTopK,
    tagJaccardMin,
    limit,
  ]);

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
        // Normalize matchScore within the result set so sizing is RELATIVE:
        // best match = 1.0, worst = 0.0. Single-hit case: assign 1.0 directly.
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

  const shown = graph?.truncation.shown ?? 0;
  const total = graph?.truncation.total ?? 0;

  return (
    <div className="relative w-screen h-screen overflow-hidden">
      {viewMode === "2d" ? <Graph2D graph={graph} /> : <Graph3D graph={graph} />}

      {/* Top bar — search, view, refresh, status. Spans width so it anchors the UI. */}
      <header className="absolute top-3 left-3 right-3 z-20 flex items-center gap-2 text-xs">
        <div className="flex items-center rounded-lg border border-panelBorder bg-panel backdrop-blur overflow-hidden">
          <button
            type="button"
            onClick={() => setSidebarOpen(!sidebarOpen)}
            className="p-2 hover:bg-white/5 text-slate-300"
            title={sidebarOpen ? "Hide sidebar" : "Show sidebar"}
            aria-label="Toggle sidebar"
          >
            <PanelLeft size={14} />
          </button>
          <div className="flex items-center gap-2 px-3 py-1.5 border-l border-panelBorder text-slate-200">
            <Brain size={14} className="text-sky-400" />
            <span className="font-semibold tracking-tight">MemoryBank</span>
          </div>
        </div>

        <div className="relative flex-1 max-w-xl">
          <Search
            size={13}
            className="absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-500 pointer-events-none"
          />
          <input
            type="text"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="Search memories (semantic + keyword)…"
            className="w-full bg-panel backdrop-blur border border-panelBorder rounded-lg pl-8 pr-8 py-2 text-sm text-slate-200 placeholder-slate-500 focus:outline-none focus:border-sky-500/60 focus:ring-1 focus:ring-sky-500/30"
          />
          {searching && (
            <Loader2
              size={13}
              className="absolute right-2.5 top-1/2 -translate-y-1/2 text-sky-400 animate-spin"
              aria-label="Searching"
            />
          )}
        </div>

        <div className="flex items-center rounded-lg border border-panelBorder bg-panel backdrop-blur overflow-hidden">
          <button
            type="button"
            onClick={() => setViewMode("2d")}
            className={`px-3 py-1.5 transition-colors ${
              viewMode === "2d"
                ? "bg-sky-500/20 text-sky-200"
                : "text-slate-400 hover:text-slate-200"
            }`}
          >
            2D
          </button>
          <button
            type="button"
            onClick={() => setViewMode("3d")}
            className={`px-3 py-1.5 transition-colors ${
              viewMode === "3d"
                ? "bg-sky-500/20 text-sky-200"
                : "text-slate-400 hover:text-slate-200"
            }`}
          >
            3D
          </button>
        </div>

        <button
          type="button"
          onClick={handleManualRefresh}
          disabled={loading}
          className="p-2 rounded-lg border border-panelBorder bg-panel backdrop-blur hover:bg-white/5 disabled:opacity-50 text-slate-300"
          title="Refresh graph and reset layout"
          aria-label="Refresh"
        >
          <RefreshCw size={14} className={loading ? "animate-spin" : ""} />
        </button>

        <div
          className="hidden sm:flex items-center rounded-lg border border-panelBorder bg-panel backdrop-blur px-3 py-1.5 text-slate-400 whitespace-nowrap"
          title={shown < total ? "Result set truncated by limit" : undefined}
        >
          <span className="text-slate-200 tabular-nums">{shown}</span>
          <span className="mx-1">/</span>
          <span className="tabular-nums">{total}</span>
          {shown < total && <span className="ml-2 text-amber-400">•</span>}
        </div>
      </header>

      {/* Focus label — appears below the top bar when a node is hovered/selected. */}
      {focusLabel && (
        <div
          className="absolute top-14 left-1/2 -translate-x-1/2 z-20 rounded-md border border-panelBorder bg-panel backdrop-blur px-3 py-1 text-xs text-slate-200 max-w-md truncate pointer-events-none"
          title={focusLabel}
        >
          {focusLabel}
        </div>
      )}

      <FilterPanel />

      <DetailPanel />

      <Legend />

      {error && (
        <div className="absolute top-14 left-1/2 -translate-x-1/2 bg-red-900/60 border border-red-700 text-red-200 px-4 py-2 rounded text-sm z-20">
          {error}
        </div>
      )}

      {graph && graph.nodes.length === 0 && !loading && (
        <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
          <div className="text-center text-slate-400 max-w-md px-6">
            <div className="text-2xl mb-2">No memories match the current filters</div>
            <div className="text-sm">
              Adjust filters in the left panel, or add memories via the MemoryBank MCP tools.
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
