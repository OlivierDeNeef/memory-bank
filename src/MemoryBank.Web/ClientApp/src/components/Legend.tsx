import { edgeColors, pinnedColor, typeColors } from "../lib/colors";

export function Legend() {
  return (
    <div className="absolute bottom-3 left-1/2 -translate-x-1/2 bg-panel backdrop-blur border border-panelBorder rounded-lg px-4 py-2 text-xs z-10 space-y-1.5 shadow-xl shadow-black/30">
      <LegendRow label="Types">
        {Object.entries(typeColors).map(([name, color]) => (
          <NodeDot key={name} color={color} label={name} />
        ))}
        <span className="flex items-center gap-1.5 text-slate-300">
          <span
            className="inline-block w-2.5 h-2.5 rounded-full shadow-[0_0_8px_rgba(253,224,71,0.8)]"
            style={{ background: pinnedColor }}
          />
          pinned
        </span>
      </LegendRow>
      <LegendRow label="Edges">
        <EdgeBar color={edgeColors.link} label="Link" />
        <EdgeBar color={edgeColors.similarity} label="Similarity" />
        <EdgeBar color={edgeColors.tags} label="Tag overlap" />
        <EdgeBar color={edgeColors.category} label="Category" />
      </LegendRow>
    </div>
  );
}

function LegendRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex items-center gap-4">
      <span className="text-slate-500 uppercase tracking-wider w-14 shrink-0">{label}</span>
      <div className="flex items-center gap-4 flex-wrap">{children}</div>
    </div>
  );
}

function NodeDot({ color, label }: { color: string; label: string }) {
  return (
    <span className="flex items-center gap-1.5 text-slate-300">
      <span
        className="inline-block w-2.5 h-2.5 rounded-full"
        style={{ background: color }}
      />
      {label}
    </span>
  );
}

function EdgeBar({ color, label }: { color: string; label: string }) {
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
