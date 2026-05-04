// Memory type → node fill color. Chosen to be visually distinct from each other
// AND from `pinnedColor`, so the pinned halo can never be confused with a type fill.
export const typeColors: Record<string, string> = {
  todo: "#60a5fa",      // blue
  decision: "#f472b6",  // pink
  reference: "#a78bfa", // violet
  guide: "#34d399",     // emerald — avoids collision with `edgeColors.tags` orange
};

const unknownTypeColor = "#94a3b8"; // slate

export function typeColor(type: string | null | undefined): string {
  if (!type) return unknownTypeColor;
  return typeColors[type] ?? unknownTypeColor;
}

// Reserved exclusively for the pinned halo — no type color uses this hue.
export const pinnedColor = "#fde047"; // yellow

export const edgeColors: Record<string, string> = {
  link: "#ffffff",
  similarity: "#22d3ee",
  tags: "#fb923c",
  category: "#475569",
};
