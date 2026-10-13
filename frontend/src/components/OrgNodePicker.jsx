import { useEffect, useMemo, useState } from 'react';

// Organization -> level 1 -> level 2 ... Each level lists the children of the previous choice.
// The nodes it receives are already limited to the user's data-flow scope by the server.
// An organization's single real root node is auto-selected and hidden (it repeats the organization name).
// The last node picked is the value. "Find by name" jumps straight to any node and fills the whole chain.
export default function OrgNodePicker({ lookups, value, onChange, error, disabled }) {
  const { organizations = [], nodes = [], nodeTypes = [] } = lookups;

  const byId = useMemo(() => new Map(nodes.map((n) => [n.id, n])), [nodes]);
  const typeName = useMemo(() => new Map(nodeTypes.map((t) => [t.id, t.name])), [nodeTypes]);
  const orgName = useMemo(() => new Map(organizations.map((o) => [o.id, o.name])), [organizations]);

  const childrenOf = useMemo(() => {
    const m = new Map();
    nodes.forEach((n) => {
      const k = n.parentId ? String(n.parentId) : `root-${n.orgId}`;
      if (!m.has(k)) m.set(k, []);
      m.get(k).push(n);
    });
    return m;
  }, [nodes]);

  // the organization's only real root node, if it has exactly one (that one is hidden)
  const hiddenRootOf = (oid) => {
    if (!oid) return null;
    const roots = childrenOf.get(`root-${oid}`) || [];
    return roots.length === 1 && roots[0].isRoot ? roots[0] : null;
  };

  const chainOf = (id) => {
    const out = [];
    let cur = byId.get(id);
    let guard = 0;
    while (cur && guard++ < 50) {
      out.unshift(cur.id);
      cur = cur.parentId ? byId.get(cur.parentId) : null;
    }
    return out;
  };

  const initial = () => {
    const node = value ? byId.get(value) : null;
    if (node) return { orgId: node.orgId, chain: chainOf(value) };
    const oid = organizations.length === 1 ? organizations[0].id : '';
    const root = hiddenRootOf(oid);
    return { orgId: oid, chain: root ? [root.id] : [] };
  };

  const [state, setState] = useState(initial);
  const [search, setSearch] = useState('');
  const { orgId, chain } = state;

  const hiddenRoot = hiddenRootOf(orgId);
  const autoRootOnly = !!hiddenRoot && chain.length === 1 && chain[0] === hiddenRoot.id;

  // the form can load the employee (or reset) after this mounts
  useEffect(() => {
    const last = chain.length ? chain[chain.length - 1] : null;
    if ((value ?? null) !== last) {
      if (value == null && autoRootOnly) return;
      setState(initial());
    }
  }, [value, nodes.length, organizations.length]); // eslint-disable-line react-hooks/exhaustive-deps

  // an auto-selected root counts as the current value until a deeper node is picked
  useEffect(() => {
    if (value == null && autoRootOnly) onChange(chain[0]);
  }, [orgId, nodes.length]); // eslint-disable-line react-hooks/exhaustive-deps

  const emit = (next) => {
    setState(next);
    onChange(next.chain.length ? next.chain[next.chain.length - 1] : null);
  };

  const selectOrg = (oid) => {
    const root = hiddenRootOf(oid);
    emit({ orgId: oid, chain: root ? [root.id] : [] });
  };

  // names of a node's chain, without the hidden root (unless the root itself is the node)
  const displayNames = (id) => {
    let ids = chainOf(id);
    const first = ids.length ? byId.get(ids[0]) : null;
    if (first && ids.length > 1 && hiddenRootOf(first.orgId)?.id === first.id) ids = ids.slice(1);
    return ids.map((x) => byId.get(x)?.name).join(' › ');
  };

  // visible levels
  const prefix = hiddenRoot ? [hiddenRoot.id] : [];
  const vchain = hiddenRoot ? chain.slice(1) : chain;
  const levels = [];
  if (orgId) {
    let opts = hiddenRoot ? childrenOf.get(String(hiddenRoot.id)) || [] : childrenOf.get(`root-${orgId}`) || [];
    let i = 0;
    while (opts.length && i < 25) {
      const names = [...new Set(opts.map((o) => typeName.get(o.typeId)).filter(Boolean))];
      levels.push({ options: opts, label: names.join(' / ') || 'Next level', selected: vchain[i] ?? '' });
      if (vchain[i] == null) break;
      opts = childrenOf.get(String(vchain[i])) || [];
      i += 1;
    }
  }

  const q = search.trim().toLowerCase();
  const results = q
    ? nodes
        .filter((n) => (!orgId || n.orgId === orgId) && (n.name.toLowerCase().includes(q) || (n.code || '').toLowerCase().includes(q)))
        .slice(0, 30)
    : [];

  const lastId = chain.length ? chain[chain.length - 1] : null;

  if (organizations.length === 0) {
    return <div className="alert alert-warning py-2 small mb-0">You have no organization node assigned, so you cannot add employees. Ask an administrator to assign you to a node.</div>;
  }

  return (
    <div>
      <div className="position-relative mb-3">
        <input
          className="form-control form-control-sm"
          placeholder="Find a node by name or code…"
          value={search}
          disabled={disabled}
          onChange={(e) => setSearch(e.target.value)}
        />
        {q && (
          <div className="list-group position-absolute w-100 shadow-sm" style={{ zIndex: 20, maxHeight: 260, overflowY: 'auto' }}>
            {results.length === 0 && <div className="list-group-item small text-muted">No match</div>}
            {results.map((n) => (
              <button
                type="button"
                key={n.id}
                className="list-group-item list-group-item-action small"
                onClick={() => { emit({ orgId: n.orgId, chain: chainOf(n.id) }); setSearch(''); }}
              >
                {organizations.length > 1 && <span className="text-muted">{orgName.get(n.orgId)} › </span>}
                {displayNames(n.id)}
              </button>
            ))}
          </div>
        )}
      </div>

      <div className="row g-2">
        <div className="col-md-4">
          <label className="form-label small fw-semibold mb-1">Organization <span className="text-danger">*</span></label>
          <select
            className={`form-select ${error && !lastId ? 'is-invalid' : ''}`}
            value={orgId}
            disabled={disabled}
            onChange={(e) => selectOrg(e.target.value ? Number(e.target.value) : '')}
          >
            <option value="">Select…</option>
            {organizations.map((o) => <option key={o.id} value={o.id}>{o.name}</option>)}
          </select>
        </div>

        {levels.map((lv, i) => (
          <div className="col-md-4" key={i}>
            <label className="form-label small fw-semibold mb-1">{lv.label}</label>
            <select
              className={`form-select ${error && autoRootOnly && i === 0 ? 'is-invalid' : ''}`}
              value={lv.selected}
              disabled={disabled}
              onChange={(e) => emit({ orgId, chain: [...prefix, ...vchain.slice(0, i), ...(e.target.value ? [Number(e.target.value)] : [])] })}
            >
              <option value="">Select…</option>
              {lv.options.map((o) => <option key={o.id} value={o.id}>{o.name}</option>)}
            </select>
          </div>
        ))}
      </div>

      {lastId && !autoRootOnly && (
        <div className="small text-muted mt-2"><i className="bi bi-diagram-3 me-1" />{orgName.get(orgId)} › {displayNames(lastId)}</div>
      )}
      {error && <div className="text-danger small mt-1">{error}</div>}
    </div>
  );
}