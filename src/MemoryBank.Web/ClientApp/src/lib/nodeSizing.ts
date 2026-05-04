import type { GraphNode } from "../api/types";

export interface SizingStrategy {
  id: string;
  label: string;
  hint: string;
  contribute: (node: GraphNode) => number;
}

export const UNIFORM_BASE_SIZE = 5;
export const MIN_SIZE = 3;

export const SIZING_STRATEGIES: SizingStrategy[] = [
  {
    id: "access",
    label: "Access frequency",
    hint: "Grows with how often the memory is recalled",
    contribute: (node) => Math.log2(node.accessCount + 1) * 1.2,
  },
  {
    id: "priority",
    label: "Priority",
    hint: "Higher priority memories are larger",
    contribute: (node) => (node.priority - 3) * 0.5,
  },
];

export function computeBaseSize(node: GraphNode, enabledIds: string[]): number {
  if (enabledIds.length === 0) return UNIFORM_BASE_SIZE;
  const enabled = new Set(enabledIds);
  let size = UNIFORM_BASE_SIZE;
  for (const strategy of SIZING_STRATEGIES) {
    if (enabled.has(strategy.id)) size += strategy.contribute(node);
  }
  return Math.max(MIN_SIZE, size);
}
