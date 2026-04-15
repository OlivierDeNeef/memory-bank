import { create } from "zustand";
import type { EdgeType, GraphQuery } from "./api/types";

interface AppState {
  // Filters
  edgeTypes: EdgeType[];
  categories: string[];
  tags: string[];
  types: string[];
  includeArchived: boolean;
  simThreshold: number;
  simTopK: number;
  tagJaccardMin: number;
  limit: number;

  // UI state
  selectedNodeId: string | null;
  hoveredNodeId: string | null;
  searchQuery: string;
  // Map of memoryId → normalized match score (0..1). null when no search active.
  searchScores: Map<string, number> | null;

  // Mutators
  setEdgeTypes: (v: EdgeType[]) => void;
  toggleEdgeType: (t: EdgeType) => void;
  setCategories: (v: string[]) => void;
  setTags: (v: string[]) => void;
  setTypes: (v: string[]) => void;
  setIncludeArchived: (v: boolean) => void;
  setSimThreshold: (v: number) => void;
  setSimTopK: (v: number) => void;
  setTagJaccardMin: (v: number) => void;
  setSelectedNode: (id: string | null) => void;
  setHoveredNode: (id: string | null) => void;
  setSearchQuery: (v: string) => void;
  setSearchScores: (scores: Map<string, number> | null) => void;

  buildGraphQuery: () => GraphQuery;
}

export const useStore = create<AppState>((set, get) => ({
  edgeTypes: ["links", "similarity"],
  categories: [],
  tags: [],
  types: [],
  includeArchived: false,
  simThreshold: 0.78,
  simTopK: 5,
  tagJaccardMin: 0.3,
  limit: 1000,

  selectedNodeId: null,
  hoveredNodeId: null,
  searchQuery: "",
  searchScores: null,

  setEdgeTypes: (v) => set({ edgeTypes: v }),
  toggleEdgeType: (t) =>
    set((s) => ({
      edgeTypes: s.edgeTypes.includes(t)
        ? s.edgeTypes.filter((x) => x !== t)
        : [...s.edgeTypes, t],
    })),
  setCategories: (v) => set({ categories: v }),
  setTags: (v) => set({ tags: v }),
  setTypes: (v) => set({ types: v }),
  setIncludeArchived: (v) => set({ includeArchived: v }),
  setSimThreshold: (v) => set({ simThreshold: v }),
  setSimTopK: (v) => set({ simTopK: v }),
  setTagJaccardMin: (v) => set({ tagJaccardMin: v }),
  setSelectedNode: (id) => set({ selectedNodeId: id }),
  setHoveredNode: (id) => set({ hoveredNodeId: id }),
  setSearchQuery: (v) => set({ searchQuery: v }),
  setSearchScores: (scores) => set({ searchScores: scores }),

  buildGraphQuery: () => {
    const s = get();
    return {
      edgeTypes: s.edgeTypes,
      categories: s.categories,
      tags: s.tags,
      types: s.types,
      includeArchived: s.includeArchived,
      simThreshold: s.simThreshold,
      simTopK: s.simTopK,
      tagJaccardMin: s.tagJaccardMin,
      limit: s.limit,
    };
  },
}));
