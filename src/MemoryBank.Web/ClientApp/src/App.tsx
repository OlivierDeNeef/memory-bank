import { useCallback, useEffect, useRef, useState } from "react";
import { fetchGraph } from "./api/client";
import type { GraphResponse } from "./api/types";
import { Graph2D } from "./components/Graph2D";
import { FilterPanel } from "./components/FilterPanel";
import { DetailPanel } from "./components/DetailPanel";
import { Legend } from "./components/Legend";
import { useStore } from "./store";

export default function App() {
  const [graph, setGraph] = useState<GraphResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Subscribe to every filter field so the effect re-runs on any change.
  const edgeTypes = useStore((s) => s.edgeTypes);
  const categories = useStore((s) => s.categories);
  const tags = useStore((s) => s.tags);
  const types = useStore((s) => s.types);
  const includeArchived = useStore((s) => s.includeArchived);
  const simThreshold = useStore((s) => s.simThreshold);
  const simTopK = useStore((s) => s.simTopK);
  const tagJaccardMin = useStore((s) => s.tagJaccardMin);
  const limit = useStore((s) => s.limit);
  const buildQuery = useStore((s) => s.buildGraphQuery);

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

  return (
    <div className="relative w-screen h-screen">
      <Graph2D graph={graph} />

      <FilterPanel
        onRefresh={refresh}
        loading={loading}
        shown={graph?.truncation.shown ?? 0}
        total={graph?.truncation.total ?? 0}
      />

      <DetailPanel />

      <Legend />

      {error && (
        <div className="absolute top-3 left-1/2 -translate-x-1/2 bg-red-900/60 border border-red-700 text-red-200 px-4 py-2 rounded text-sm z-20">
          {error}
        </div>
      )}

      {graph && graph.nodes.length === 0 && !loading && (
        <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
          <div className="text-center text-slate-400 max-w-md px-6">
            <div className="text-2xl mb-2">No memories match the current filters</div>
            <div className="text-sm">
              Adjust filters in the left panel, or add memories via the DeepMind MCP tools.
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
