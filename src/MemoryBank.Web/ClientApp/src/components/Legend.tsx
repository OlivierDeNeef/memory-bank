import { edgeColors } from "../lib/colors";

export function Legend() {
  return (
    <div className="absolute bottom-3 left-1/2 -translate-x-1/2 bg-panel backdrop-blur border border-panelBorder rounded-full px-4 py-2 text-xs flex items-center gap-4 z-10">
      <LegendDot color={edgeColors.link} label="Link" />
      <LegendDot color={edgeColors.similarity} label="Similarity" />
      <LegendDot color={edgeColors.tags} label="Tag overlap" />
      <LegendDot color={edgeColors.category} label="Category" />
      <span className="w-px h-3 bg-slate-700" />
      <span className="text-slate-400">Pinned</span>
      <span className="w-2.5 h-2.5 rounded-full bg-yellow-300 shadow-[0_0_8px_rgba(253,224,71,0.8)]" />
    </div>
  );
}

function LegendDot({ color, label }: { color: string; label: string }) {
  return (
    <span className="flex items-center gap-1.5 text-slate-300">
      <span
        className="inline-block w-4 h-0.5 rounded"
        style={{ background: color }}
      />
      {label}
    </span>
  );
}
