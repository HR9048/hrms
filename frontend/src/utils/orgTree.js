// Builds a nested tree from the flat list returned by GET /api/organization/hierarchy.
// Each row has node_id / parent_node_id / node_type_id (+ type_name, type_code, ...).
// This mirrors how MenuItem.jsx already renders an arbitrarily deep tree from
// a similar flat parent/child shape — same idea, applied to org nodes.
export function buildOrgTree(flatNodes, parentId = null) {
  return flatNodes
    .filter((n) => (n.parent_node_id ?? null) === parentId)
    .sort((a, b) => (a.display_order ?? 0) - (b.display_order ?? 0) || a.node_name.localeCompare(b.node_name))
    .map((n) => ({
      ...n,
      children: buildOrgTree(flatNodes, n.node_id)
    }));
}

// All descendant node_ids of a given node (used client-side to grey out
// invalid "move" targets before the user even submits).
export function getDescendantIds(flatNodes, nodeId) {
  const ids = new Set();
  const queue = [nodeId];
  while (queue.length) {
    const current = queue.shift();
    flatNodes
      .filter((n) => n.parent_node_id === current)
      .forEach((child) => {
        if (!ids.has(child.node_id)) {
          ids.add(child.node_id);
          queue.push(child.node_id);
        }
      });
  }
  return ids;
}
