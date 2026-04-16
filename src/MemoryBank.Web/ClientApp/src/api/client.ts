import type {
  FilterOptions,
  GraphQuery,
  GraphResponse,
  MemoryDetail,
  SearchHit,
} from "./types";

async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(url, { signal });
  if (!response.ok) {
    throw new Error(`${response.status} ${response.statusText} — ${url}`);
  }
  return response.json();
}

export function fetchFilters(): Promise<FilterOptions> {
  return getJson<FilterOptions>("/api/filters");
}

export function fetchGraph(query: GraphQuery, signal?: AbortSignal): Promise<GraphResponse> {
  const params = new URLSearchParams();
  params.set("edgeTypes", query.edgeTypes.join(","));
  if (query.categories.length > 0) params.set("categories", query.categories.join(","));
  if (query.tags.length > 0) params.set("tags", query.tags.join(","));
  if (query.types.length > 0) params.set("types", query.types.join(","));
  params.set("includeArchived", String(query.includeArchived));
  params.set("simThreshold", String(query.simThreshold));
  params.set("simTopK", String(query.simTopK));
  params.set("tagJaccardMin", String(query.tagJaccardMin));
  params.set("limit", String(query.limit));
  return getJson<GraphResponse>(`/api/graph?${params.toString()}`, signal);
}

export function fetchMemoryDetail(id: string): Promise<MemoryDetail> {
  return getJson<MemoryDetail>(`/api/memory/${encodeURIComponent(id)}`);
}

export function searchMemories(q: string, signal?: AbortSignal): Promise<SearchHit[]> {
  if (!q.trim()) return Promise.resolve([]);
  const params = new URLSearchParams({ q });
  return getJson<SearchHit[]>(`/api/search?${params.toString()}`, signal);
}
