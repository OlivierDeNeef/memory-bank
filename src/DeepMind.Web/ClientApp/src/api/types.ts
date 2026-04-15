export type EdgeType = "links" | "similarity" | "tags" | "category";

export interface GraphNode {
  id: string;
  label: string;
  type: string;
  categoryId: string | null;
  categoryPath: string | null;
  tags: string[];
  priority: number;
  pinned: boolean;
  accessCount: number;
  createdAt: string;
}

export interface GraphEdge {
  source: string;
  target: string;
  type: "link" | "similarity" | "tags" | "category";
  weight: number;
  linkType: string | null;
}

export interface GraphTruncation {
  total: number;
  shown: number;
}

export interface GraphResponse {
  nodes: GraphNode[];
  edges: GraphEdge[];
  truncation: GraphTruncation;
}

export interface CategoryOption {
  id: string;
  path: string;
  name: string;
  memoryCount: number;
}

export interface TagOption {
  name: string;
  count: number;
}

export interface FilterOptions {
  categories: CategoryOption[];
  tags: TagOption[];
  types: string[];
  memoryCount: number;
  embeddingsAvailable: boolean;
}

export interface LinkedMemory {
  id: string;
  label: string;
  linkType: string;
  direction: "in" | "out";
}

export interface RevisionSummary {
  number: number;
  reason: string | null;
  createdAt: string;
  contentPreview: string;
}

export interface ChunkSummary {
  id: string;
  index: number;
  summary: string | null;
  contentPreview: string;
}

export interface MemoryDetail {
  id: string;
  content: string;
  summary: string | null;
  type: string;
  categoryPath: string | null;
  tags: string[];
  priority: number;
  pinned: boolean;
  archived: boolean;
  accessCount: number;
  revisionNumber: number;
  createdAt: string;
  updatedAt: string;
  lastAccessed: string | null;
  linkedMemories: LinkedMemory[];
  revisions: RevisionSummary[];
  chunks: ChunkSummary[];
}

export interface SearchHit {
  id: string;
  /** Pure content match quality, 0..1. Best of vector and keyword similarity. */
  matchScore: number;
  vectorScore: number;
  keywordScore: number;
}

export interface GraphQuery {
  edgeTypes: EdgeType[];
  categories: string[];
  tags: string[];
  types: string[];
  includeArchived: boolean;
  simThreshold: number;
  simTopK: number;
  tagJaccardMin: number;
  limit: number;
}
