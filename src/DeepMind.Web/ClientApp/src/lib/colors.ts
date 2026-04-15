// Deterministic color per category path (stable across loads)
const palette = [
  "#60a5fa", // blue
  "#f472b6", // pink
  "#34d399", // emerald
  "#fbbf24", // amber
  "#a78bfa", // violet
  "#f87171", // red
  "#22d3ee", // cyan
  "#fb923c", // orange
  "#4ade80", // green
  "#e879f9", // fuchsia
  "#94a3b8", // slate
  "#fde047", // yellow
];

function hashString(s: string): number {
  let h = 2166136261;
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  return h >>> 0;
}

export function categoryColor(path: string | null | undefined): string {
  if (!path) return "#6b7280"; // uncategorized = gray
  return palette[hashString(path) % palette.length];
}

export const edgeColors: Record<string, string> = {
  link: "#ffffff",
  similarity: "#22d3ee",
  tags: "#fb923c",
  category: "#475569",
};
