import { useMemo, useState } from "react";

const pidOf = (n) => n.parentId ?? n.parent_id ?? n.parentNodeId ?? null;
const typeIdOf = (n) => n.nodeTypeId ?? n.node_type_id ?? n.typeId;

function buildTree(destinations) {
  const nodes = destinations?.nodes || [];
  const byId = new Map(nodes.map((n) => [n.id, n]));
  const children = new Map();
  nodes.forEach((n) => {
    const p = pidOf(n);
    const k = p != null && byId.has(p) ? p : null; // null => root level
    if (!children.has(k)) children.set(k, []);
    children.get(k).push(n);
  });
  const roots = children.get(null) || [];
  const top = roots.flatMap((r) => children.get(r.id) || []); // everything directly under root(s)
  return { nodes, byId, children, roots, top, hasTree: nodes.some((n) => pidOf(n) != null) };
}

// ancestors of a node excluding the root, top-down, including the node itself
function chainFor(tree, id) {
  const out = [];
  let cur = tree.byId.get(id);
  while (cur && pidOf(cur) != null && tree.byId.has(pidOf(cur))) {
    out.unshift(cur.id);
    cur = tree.byId.get(pidOf(cur));
  }
  return out;
}

export const findNode = (destinations, id) => (destinations?.nodes || []).find((n) => String(n.id) === String(id));

/** "Parent › Child" label for a node, root excluded. */
export const destLabel = (destinations, id) => {
  const tree = buildTree(destinations);
  const n = tree.byId.get(Number(id)) ?? findNode(destinations, id);
  if (!n) return "";
  const chain = chainFor(tree, n.id);
  return chain.length ? chain.map((x) => tree.byId.get(x).name).join(" › ") : n.name;
};

/**
 * Cascading destination picker. Only emits a LEAF node id (a node with no children);
 * while a non-leaf is selected another select appears with its children and onChange(null) is emitted.
 */
export default function DestPicker({ destinations, value, onChange, disabled }) {
  const tree = useMemo(() => buildTree(destinations), [destinations]);
  const typeName = useMemo(() => {
    const m = new Map((destinations?.nodeTypes || []).map((t) => [t.id, t.name]));
    return (n, fallback) => m.get(typeIdOf(n)) || fallback;
  }, [destinations]);
  const [chain, setChain] = useState(() => (value ? chainFor(tree, value) : []));

  // flat fallback if nodes carry no parent info
  if (!tree.hasTree) {
    return (
      <select className="form-select" value={value ?? ""} disabled={disabled} onChange={(e) => onChange(e.target.value ? Number(e.target.value) : null)}>
        <option value="">— Select destination —</option>
        {tree.nodes.map((n) => (
          <option key={n.id} value={n.id}>
            {n.name}
          </option>
        ))}
      </select>
    );
  }

  const levels = [];
  for (let i = 0; i <= chain.length; i++) {
    const opts = i === 0 ? tree.top : tree.children.get(chain[i - 1]) || [];
    if (opts.length === 0) break;
    levels.push({ i, opts });
  }

  const pick = (i, raw) => {
    const id = raw ? Number(raw) : null;
    const next = chain.slice(0, i).concat(id ? [id] : []);
    setChain(next);
    const isLeaf = id && !(tree.children.get(id) || []).length;
    onChange(isLeaf ? id : null);
  };

  return (
    <div className="row g-2">
      {levels.map(({ i, opts }) => (
        <div className="col-md-6" key={i}>
          <label className="form-label small text-muted mb-1">{typeName(opts[0], i === 0 ? "Parent node" : "Node")}</label>
          <select className="form-select" value={chain[i] ?? ""} disabled={disabled} onChange={(e) => pick(i, e.target.value)}>
            <option value="">— Select —</option>
            {opts.map((n) => (
              <option key={n.id} value={n.id}>
                {n.name}
                {(tree.children.get(n.id) || []).length ? "  ›" : ""}
              </option>
            ))}
          </select>
        </div>
      ))}
      {chain.length > 0 && !value && (
        <div className="col-12 form-text text-warning">Keep selecting until you reach the final location (one with no sub-locations).</div>
      )}
    </div>
  );
}