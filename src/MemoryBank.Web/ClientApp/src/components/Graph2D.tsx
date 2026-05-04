import { useEffect, useMemo, useRef, useState } from "react";
import ForceGraph2D, { ForceGraphMethods } from "react-force-graph-2d";
import { forceCollide, forceManyBody, type ForceLink } from "d3-force";
import type { GraphEdge, GraphNode, GraphResponse } from "../api/types";
import { edgeColors, pinnedColor, typeColor } from "../lib/colors";
import { computeBaseSize, UNIFORM_BASE_SIZE } from "../lib/nodeSizing";
import { useStore } from "../store";

type FGNode = GraphNode & { x?: number; y?: number };
type FGLink = GraphEdge & { source: string | FGNode; target: string | FGNode };

interface Props {
  graph: GraphResponse | null;
}

export function Graph2D({ graph }: Props) {
  const fgRef = useRef<ForceGraphMethods<FGNode, FGLink> | undefined>(undefined);
  const selectedNodeId = useStore((s) => s.selectedNodeId);
  const hoveredNodeId = useStore((s) => s.hoveredNodeId);
  const searchScores = useStore((s) => s.searchScores);
  const sizingStrategies = useStore((s) => s.sizingStrategies);
  const layoutResetToken = useStore((s) => s.layoutResetToken);
  const simThreshold = useStore((s) => s.simThreshold);
  const tagJaccardMin = useStore((s) => s.tagJaccardMin);
  const setSelectedNode = useStore((s) => s.setSelectedNode);
  const setHoveredNode = useStore((s) => s.setHoveredNode);

  // Neighbor adjacency for highlight dimming
  const adjacency = useMemo(() => {
    const map = new Map<string, Set<string>>();
    if (!graph) return map;
    for (const e of graph.edges) {
      const s = endpointId(e.source);
      const t = endpointId(e.target);
      if (!map.has(s)) map.set(s, new Set());
      if (!map.has(t)) map.set(t, new Set());
      map.get(s)!.add(t);
      map.get(t)!.add(s);
    }
    return map;
  }, [graph]);

  // Ease camera toward selected node
  useEffect(() => {
    if (!fgRef.current || !graph || !selectedNodeId) return;
    const node = graph.nodes.find((n) => n.id === selectedNodeId) as FGNode | undefined;
    if (!node || node.x == null || node.y == null) return;
    fgRef.current.centerAt(node.x, node.y, 800);
    fgRef.current.zoom(2.5, 800);
  }, [selectedNodeId, graph]);

  // Stable layout setup — runs only when the graph itself changes (new nodes/edges).
  // Tuned to minimize edge crossings: strong link pull brings connected nodes together,
  // and variable link distance (stronger edges → shorter distance) lets tight clusters
  // form naturally so weakly-related nodes drift to the periphery instead of cutting
  // through clusters.
  useEffect(() => {
    if (!fgRef.current) return;

    fgRef.current.d3Force("charge", buildChargeForce());

    const linkForce = fgRef.current.d3Force("link") as ForceLink<FGNode, FGLink> | null;
    if (linkForce) {
      // Distance: strong edge (weight=1) → 60px, weak edge (at threshold) → 200px.
      // This rewards tightly-related pairs with proximity and pushes weak links to the edge.
      linkForce
        .distance((l: FGLink) => 200 - edgeStrength(l) * 140)
        .strength((l: FGLink) => 0.4 + edgeStrength(l) * 0.5);
    }

    fgRef.current.d3Force(
      "collide",
      forceCollide<FGNode>((node) => nodeSize(node) + 6).strength(0.9).iterations(2)
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [graph]);

  // When search scores arrive, both the collide radius AND the charge strength need to
  // refresh: d3's forceManyBody caches the per-node strength at .initialize() time, so
  // the size-aware strength fn must be re-applied for bigger (search-matched) nodes to
  // actually push their neighbors away with more force. The strong damping
  // (d3VelocityDecay=0.7) keeps the resulting reheat short and gentle.
  // Sizing-strategy toggles go through the same path for the same reason.
  const skipNextEffect = useRef(true); // skip on initial mount
  useEffect(() => {
    if (!fgRef.current) return;
    if (skipNextEffect.current) {
      skipNextEffect.current = false;
      return;
    }

    fgRef.current.d3Force(
      "collide",
      forceCollide<FGNode>((node) => nodeSize(node) + 6).strength(0.9).iterations(2)
    );
    fgRef.current.d3Force("charge", buildChargeForce());
    fgRef.current.d3ReheatSimulation();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchScores, sizingStrategies]);

  // Shared charge-force factory. Strength scales linearly with node radius so a node
  // drawn 6× larger during search repels neighbors ~6× harder — the visible gap around
  // it then grows in step with the node itself, instead of shrinking because the center
  // moved but the force stayed flat.
  function buildChargeForce() {
    return forceManyBody<FGNode>()
      .strength((node) => -160 * (nodeSize(node) / UNIFORM_BASE_SIZE))
      .distanceMax(360);
  }

  // Position cache: when a filter change causes a refetch, react-force-graph replaces the
  // graphData and re-heats the simulation at alpha=1. Without seeded positions, nodes start
  // from wherever the old references put them (or random) and visibly jump. By carrying
  // x/y/vx/vy forward by id, the refreshed simulation begins at the previous steady state,
  // so only the delta (new/removed edges) produces motion.
  const prevNodesRef = useRef<Map<string, FGNode>>(new Map());
  const lastResetToken = useRef(layoutResetToken);
  const [hasMounted, setHasMounted] = useState(false);

  const data = useMemo(() => {
    // Manual refresh bumps the token → drop cached positions so the simulation
    // re-lays out from scratch instead of inheriting drifted/velocity-carrying state.
    if (lastResetToken.current !== layoutResetToken) {
      prevNodesRef.current = new Map();
      lastResetToken.current = layoutResetToken;
    }
    if (!graph) {
      prevNodesRef.current = new Map();
      return { nodes: [], links: [] };
    }
    const prior = prevNodesRef.current;
    const nodes = graph.nodes.map((n) => {
      const next: FGNode = { ...n };
      const p = prior.get(n.id);
      if (p?.x != null && p?.y != null) {
        next.x = p.x;
        next.y = p.y;
        (next as FGNode & { vx?: number; vy?: number }).vx =
          (p as FGNode & { vx?: number }).vx ?? 0;
        (next as FGNode & { vx?: number; vy?: number }).vy =
          (p as FGNode & { vy?: number }).vy ?? 0;
      }
      return next;
    });
    // Store references to the new nodes; the simulation mutates x/y on these objects,
    // so the cache stays current for the next refetch.
    prevNodesRef.current = new Map(nodes.map((n) => [n.id, n]));
    return {
      nodes,
      links: graph.edges.map((e) => ({ ...e })) as FGLink[],
    };
  }, [graph, layoutResetToken]);

  // After the first graph renders, drop warmup ticks to zero so subsequent refetches
  // animate visibly instead of running warmup invisibly (which reads as a snap).
  useEffect(() => {
    if (graph && !hasMounted) setHasMounted(true);
  }, [graph, hasMounted]);

  const searchActive = searchScores !== null;
  const focus = hoveredNodeId ?? selectedNodeId;

  const isDimmed = (nodeId: string): boolean => {
    if (searchActive && !searchScores!.has(nodeId)) return true;
    if (focus && focus !== nodeId && !adjacency.get(focus)?.has(nodeId)) return true;
    return false;
  };

  const nodeSize = (node: FGNode) => {
    const baseSize = computeBaseSize(node, sizingStrategies);

    if (!searchActive) return baseSize;

    const score = searchScores!.get(node.id);
    if (score == null) {
      // Non-matching nodes shrink aggressively so matches visually dominate.
      return baseSize * 0.35;
    }
    // Score here is the RELATIVE rank within the search result set (0..1 via min-max).
    // Worst match stays at baseline (1×), best match reaches 6× — a dramatic spread that
    // makes the "winner" instantly recognizable while weaker matches stay proportional.
    return baseSize * (1 + score * 5);
  };

  const drawNode = (node: FGNode, ctx: CanvasRenderingContext2D, globalScale: number) => {
    const size = nodeSize(node);
    const color = typeColor(node.type);
    const dimmed = isDimmed(node.id);
    const isFocused = focus === node.id;

    ctx.globalAlpha = dimmed ? 0.2 : 1;

    // Pinned: soft halo underneath the color glow, in the reserved pinned hue
    // (no type color uses this yellow, so the halo is unambiguous).
    if (node.pinned) {
      const haloGrad = ctx.createRadialGradient(node.x!, node.y!, size, node.x!, node.y!, size * 2.2);
      haloGrad.addColorStop(0, withAlpha(pinnedColor, 0.35));
      haloGrad.addColorStop(1, withAlpha(pinnedColor, 0));
      ctx.fillStyle = haloGrad;
      ctx.beginPath();
      ctx.arc(node.x!, node.y!, size * 2.2, 0, Math.PI * 2);
      ctx.fill();
    }

    // Bloom: native canvas shadow in the node's color. Focused and larger nodes glow more.
    // Dimmed nodes get a subtle glow so they still read as graph elements, not artifacts.
    const bloomBase = dimmed ? 4 : isFocused ? 22 : 12;
    ctx.shadowBlur = bloomBase + size * 0.6;
    ctx.shadowColor = color;

    // Main filled circle (picks up the shadow as a bloom halo)
    ctx.fillStyle = color;
    ctx.beginPath();
    ctx.arc(node.x!, node.y!, size, 0, Math.PI * 2);
    ctx.fill();

    // Clear shadow before drawing the outline so it stays crisp
    ctx.shadowBlur = 0;
    ctx.shadowColor = "transparent";

    // Outline — slightly lighter when focused
    ctx.strokeStyle = isFocused ? "rgba(255,255,255,0.9)" : "rgba(255,255,255,0.25)";
    ctx.lineWidth = isFocused ? 2 / globalScale : 1 / globalScale;
    ctx.stroke();

    ctx.globalAlpha = 1;
  };

  // Custom pointer hit area (the label doesn't intercept clicks)
  const nodePointerAreaPaint = (node: FGNode, color: string, ctx: CanvasRenderingContext2D) => {
    const size = nodeSize(node);
    ctx.fillStyle = color;
    ctx.beginPath();
    ctx.arc(node.x!, node.y!, size + 2, 0, Math.PI * 2);
    ctx.fill();
  };

  // Edge strength is normalized relative to its filter threshold so the full visual range
  // is used for the edges that actually appear. Example: with simThreshold=0.78, a cosine
  // score of 0.78 maps to 0 (thinnest) and 1.0 maps to 1 (thickest); edges below 0.78
  // don't exist in the graph. Without this remapping, every visible similarity edge would
  // fall in the top ~22% of the thickness range and look nearly identical.
  const edgeStrength = (l: FGLink): number => {
    const normalize = (weight: number, threshold: number): number => {
      const clamped = Math.max(threshold, Math.min(1, weight));
      const range = Math.max(0.0001, 1 - threshold);
      return (clamped - threshold) / range;
    };
    switch (l.type) {
      case "similarity":
        return normalize(l.weight, simThreshold);
      case "tags":
        return normalize(l.weight, tagJaccardMin);
      case "link":
        return 0.85; // explicit links — consistently prominent (binary, no slider)
      case "category":
        return 0.35; // structural — subtle (binary, no slider)
      default:
        return 0.5;
    }
  };

  const linkColor = (link: object) => {
    const l = link as FGLink;
    const base = edgeColors[l.type] || "#94a3b8";
    const sId = endpointId(l.source);
    const tId = endpointId(l.target);
    if (focus && sId !== focus && tId !== focus) return withAlpha(base, 0.05);
    // Opacity tracks strength linearly across the full 0.1–1.0 range. Strong relations
    // render solid; weak ones fade almost to the background.
    const alpha = 0.1 + edgeStrength(l) * 0.9;
    return withAlpha(base, alpha);
  };

  const linkWidth = (link: object) => {
    const l = link as FGLink;
    const sId = endpointId(l.source);
    const tId = endpointId(l.target);
    const isFocused = focus && (sId === focus || tId === focus);
    // Non-linear (strength^1.4) curve maps the 0..1 strength into the 0.3..8 thickness range,
    // emphasizing the gap between strong and weak relations.
    const strength = edgeStrength(l);
    const base = 0.3 + Math.pow(strength, 1.4) * 7.7;
    return isFocused ? base + 2 : base;
  };

  return (
    <ForceGraph2D
      ref={fgRef}
      graphData={data}
      backgroundColor="#050710"
      nodeCanvasObject={drawNode}
      nodePointerAreaPaint={nodePointerAreaPaint}
      linkColor={linkColor}
      linkWidth={linkWidth}
      linkDirectionalParticles={(l: object) => ((l as FGLink).type === "link" ? 2 : 0)}
      linkDirectionalParticleWidth={1.5}
      linkDirectionalParticleSpeed={0.006}
      onNodeHover={(node) => setHoveredNode((node as FGNode | null)?.id ?? null)}
      onNodeClick={(node) => setSelectedNode((node as FGNode).id)}
      onNodeDrag={() => fgRef.current?.d3ReheatSimulation()}
      onBackgroundClick={() => setSelectedNode(null)}
      cooldownTicks={300}
      warmupTicks={hasMounted ? 0 : 60}
      // High damping + fast alpha decay → when search reheats the simulation, nodes only
      // shift a few pixels to resolve new overlaps instead of drifting across the canvas.
      d3VelocityDecay={0.7}
      d3AlphaDecay={0.08}
      d3AlphaMin={0.05}
    />
  );
}

// ── Helpers ────────────────────────────────────────────────────────────

function endpointId(endpoint: string | FGNode): string {
  return typeof endpoint === "string" ? endpoint : endpoint.id;
}

function withAlpha(hex: string, alpha: number): string {
  const h = hex.replace("#", "");
  const r = parseInt(h.substring(0, 2), 16);
  const g = parseInt(h.substring(2, 4), 16);
  const b = parseInt(h.substring(4, 6), 16);
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}
