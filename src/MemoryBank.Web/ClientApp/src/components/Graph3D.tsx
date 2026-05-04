import { useEffect, useMemo, useRef, useState } from "react";
import ForceGraph3D, { ForceGraphMethods } from "react-force-graph-3d";
import * as THREE from "three";
import type { GraphEdge, GraphNode, GraphResponse } from "../api/types";
import { edgeColors, pinnedColor, typeColor } from "../lib/colors";
import { computeBaseSize, UNIFORM_BASE_SIZE } from "../lib/nodeSizing";
import { useStore } from "../store";

type FGNode = GraphNode & { x?: number; y?: number; z?: number };
type FGLink = GraphEdge & { source: string | FGNode; target: string | FGNode };

interface Props {
  graph: GraphResponse | null;
}

export function Graph3D({ graph }: Props) {
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

  // Layout tuning — mirrors the 2D component:
  //   • link distance and strength vary with edge weight so strong edges pull tightly
  //     and weak edges drift to the periphery (reduces edge crossings / untangles)
  //   • charge strength scales with node radius so a search-boosted (larger) node
  //     pushes its neighbors away proportionally to its size
  // react-force-graph-3d ships its own 3D-aware force instances, so we mutate the
  // existing forces in place instead of importing 2D d3-force replacements.
  useEffect(() => {
    if (!fgRef.current) return;
    applyForces();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [graph]);

  // d3-force caches per-node strength at .initialize() time, so when search resizes
  // nodes (or the user toggles a sizing strategy) we must re-apply the size-aware
  // charge for the new sizes to take effect.
  const skipSearchEffect = useRef(true);
  useEffect(() => {
    if (!fgRef.current) return;
    if (skipSearchEffect.current) {
      skipSearchEffect.current = false;
      return;
    }
    applyForces();
    fgRef.current.d3ReheatSimulation();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchScores, sizingStrategies]);

  function applyForces() {
    if (!fgRef.current) return;

    const charge = fgRef.current.d3Force("charge") as unknown as
      | { strength: (s: number | ((n: FGNode) => number)) => unknown }
      | undefined;
    charge?.strength((node: FGNode) => -260 * (nodeSize(node) / UNIFORM_BASE_SIZE));

    const linkForce = fgRef.current.d3Force("link") as unknown as
      | {
          distance: (d: number | ((l: FGLink) => number)) => unknown;
          strength: (s: number | ((l: FGLink) => number)) => unknown;
        }
      | undefined;
    linkForce?.distance((l: FGLink) => 200 - edgeStrength(l) * 140);
    linkForce?.strength((l: FGLink) => 0.4 + edgeStrength(l) * 0.5);
  }

  // Fly the camera to look at the selected node from a fixed offset.
  useEffect(() => {
    if (!fgRef.current || !graph || !selectedNodeId) return;
    const node = graph.nodes.find((n) => n.id === selectedNodeId) as FGNode | undefined;
    if (!node || node.x == null || node.y == null || node.z == null) return;

    const distance = 120;
    const dist = Math.hypot(node.x, node.y, node.z) || 1;
    fgRef.current.cameraPosition(
      {
        x: node.x * (1 + distance / dist),
        y: node.y * (1 + distance / dist),
        z: node.z * (1 + distance / dist),
      },
      { x: node.x, y: node.y, z: node.z },
      800
    );
  }, [selectedNodeId, graph]);

  // Position cache: carry x/y/z (and velocity) forward across refetches so changing an
  // edge-type filter animates smoothly instead of snapping. Mirror of the 2D version,
  // with z included since the 3D simulation runs in 3 dimensions.
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
      if (p?.x != null && p?.y != null && p?.z != null) {
        next.x = p.x;
        next.y = p.y;
        next.z = p.z;
        type V = FGNode & { vx?: number; vy?: number; vz?: number };
        (next as V).vx = (p as V).vx ?? 0;
        (next as V).vy = (p as V).vy ?? 0;
        (next as V).vz = (p as V).vz ?? 0;
      }
      return next;
    });
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
    if (score == null) return baseSize * 0.35;
    return baseSize * (1 + score * 5);
  };

  // Build a sphere mesh per node. A focused node gets a halo; pinned gets a yellow ring.
  const nodeThreeObject = (node: FGNode) => {
    const size = nodeSize(node);
    const color = typeColor(node.type);
    const dimmed = isDimmed(node.id);
    const isFocused = focus === node.id;

    const group = new THREE.Group();

    const sphere = new THREE.Mesh(
      new THREE.SphereGeometry(size, 20, 20),
      new THREE.MeshBasicMaterial({
        color,
        transparent: true,
        opacity: dimmed ? 0.2 : 1,
      })
    );
    group.add(sphere);

    // Bloom ring: a semi-transparent larger sphere gives a glow feel without post-processing.
    if (!dimmed) {
      const glow = new THREE.Mesh(
        new THREE.SphereGeometry(size * (isFocused ? 1.8 : 1.3), 20, 20),
        new THREE.MeshBasicMaterial({
          color,
          transparent: true,
          opacity: isFocused ? 0.25 : 0.12,
        })
      );
      group.add(glow);
    }

    if (node.pinned) {
      const halo = new THREE.Mesh(
        new THREE.SphereGeometry(size * 2.2, 20, 20),
        new THREE.MeshBasicMaterial({
          color: pinnedColor,
          transparent: true,
          opacity: 0.15,
        })
      );
      group.add(halo);
    }

    return group;
  };

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
        return 0.85;
      case "category":
        return 0.35;
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
    const alpha = 0.1 + edgeStrength(l) * 0.9;
    return withAlpha(base, alpha);
  };

  const linkWidth = (link: object) => {
    const l = link as FGLink;
    const sId = endpointId(l.source);
    const tId = endpointId(l.target);
    const isFocused = focus && (sId === focus || tId === focus);
    const strength = edgeStrength(l);
    const base = 0.3 + Math.pow(strength, 1.4) * 2.7;
    return isFocused ? base + 1 : base;
  };

  return (
    <ForceGraph3D
      ref={fgRef}
      graphData={data}
      backgroundColor="#050710"
      nodeThreeObject={nodeThreeObject}
      nodeThreeObjectExtend={false}
      linkColor={linkColor}
      linkWidth={linkWidth}
      linkOpacity={1}
      linkDirectionalParticles={(l: object) => ((l as FGLink).type === "link" ? 2 : 0)}
      linkDirectionalParticleWidth={1.5}
      linkDirectionalParticleSpeed={0.006}
      onNodeHover={(node) => setHoveredNode((node as FGNode | null)?.id ?? null)}
      onNodeClick={(node) => setSelectedNode((node as FGNode).id)}
      onNodeDrag={() => fgRef.current?.d3ReheatSimulation()}
      onBackgroundClick={() => setSelectedNode(null)}
      cooldownTicks={300}
      warmupTicks={hasMounted ? 0 : 60}
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
