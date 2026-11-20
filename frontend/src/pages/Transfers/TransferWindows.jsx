import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import Swal from 'sweetalert2';
import { getWindowOptions, listWindows, getWindow, saveWindow, deleteWindow, windowAction, resolveObjection } from '../../api/transfers';
import useOrgScope from '../../hooks/useOrgScope';
import OrgNodeSelect from '../../components/OrgNodeSelect';
import { errMsg, fmtDateTime, StatusBadge } from './TransferBits';

const blank = { name: '', typeKey: '', scopeNodeId: '', applyFrom: '', applyTo: '', objectionFrom: '', objectionTo: '', ordersFrom: '', effectiveOn: '', totalSeats: '', generalCategory: '', overflowToGeneral: true, quotas: [] };

function phase(w) {
  const now = new Date();
  const t = (v) => (v ? new Date(v) : null);
  if (w.status === 'CLOSED') return 'Closed';
  if (w.status === 'DRAFT') return 'Not published';
  if (now < t(w.applyFrom)) return 'Applications open soon';
  if (now <= t(w.applyTo)) return 'Applications open';
  if (w.objectionFrom && now >= t(w.objectionFrom) && now <= t(w.objectionTo)) return 'Objection period';
  return 'After applications';
}

function WindowForm({ initial, options, scope, onDone, onCancel }) {
  const [f, setF] = useState(initial);
  const [saving, setSaving] = useState(false);
  const set = (k, v) => setF((p) => ({ ...p, [k]: v }));
  const quota = (k) => f.quotas.find((q) => q.categoryKey === k)?.maxSeats ?? '';
  const setQuota = (k, v) => set('quotas', [...f.quotas.filter((q) => q.categoryKey !== k), ...(v === '' ? [] : [{ categoryKey: k, maxSeats: Number(v) }])]);

  async function submit(e) {
    e.preventDefault();
    setSaving(true);
    try {
      await saveWindow(initial.id, { ...f, scopeNodeId: f.scopeNodeId ? Number(f.scopeNodeId) : null, totalSeats: f.totalSeats === '' ? null : Number(f.totalSeats), objectionFrom: f.objectionFrom || null, objectionTo: f.objectionTo || null, ordersFrom: f.ordersFrom || null, effectiveOn: f.effectiveOn || null, generalCategory: f.generalCategory || null });
      onDone();
    } catch (err) { Swal.fire('Unable to save', errMsg(err), 'error'); } finally { setSaving(false); }
  }

  const dt = (k, label, req) => (
    <div className="col-md-4"><label className="form-label small mb-1">{label}{req && ' *'}</label><input type="datetime-local" className="form-control form-control-sm" required={req} value={f[k] || ''} onChange={(e) => set(k, e.target.value)} /></div>
  );

  return (
    <form className="card mb-3 border-primary" onSubmit={submit}>
      <div className="card-header fw-semibold">{initial.id ? 'Edit window' : 'New transfer window'}</div>
      <div className="card-body row g-3">
        <div className="col-md-5"><label className="form-label small mb-1">Name *</label><input className="form-control form-control-sm" required value={f.name} onChange={(e) => set('name', e.target.value)} placeholder="e.g. General Transfer 2027" /></div>
        <div className="col-md-4">
          <label className="form-label small mb-1">Transfer type *</label>
          <select className="form-select form-select-sm" required value={f.typeKey} onChange={(e) => set('typeKey', e.target.value)}>
            <option value="">Select…</option>{options.types.map((t) => <option key={t.typeKey} value={t.typeKey}>{t.name}</option>)}
          </select>
        </div>
        <div className="col-md-3"><label className="form-label small mb-1">Open only for employees under</label><OrgNodeSelect lookups={scope} value={f.scopeNodeId} onChange={(v) => set('scopeNodeId', v)} placeholder="Everyone" /></div>

        <div className="col-12"><div className="text-uppercase text-muted small fw-bold border-bottom pb-1">Timeline (date and time)</div></div>
        {dt('applyFrom', 'Applications open', true)}{dt('applyTo', 'Applications close', true)}<div className="col-md-4" />
        {dt('objectionFrom', 'Objections open')}{dt('objectionTo', 'Objections close')}{dt('ordersFrom', 'Orders can be issued from')}
        <div className="col-md-4"><label className="form-label small mb-1">Postings take effect on</label><input type="date" className="form-control form-control-sm" value={f.effectiveOn || ''} onChange={(e) => set('effectiveOn', e.target.value)} /></div>

        <div className="col-12"><div className="text-uppercase text-muted small fw-bold border-bottom pb-1">Seats</div></div>
        <div className="col-md-3"><label className="form-label small mb-1">Total seats (blank = no limit)</label><input type="number" min="0" className="form-control form-control-sm" value={f.totalSeats} onChange={(e) => set('totalSeats', e.target.value)} /></div>
        <div className="col-md-3">
          <label className="form-label small mb-1">General category</label>
          <select className="form-select form-select-sm" value={f.generalCategory} onChange={(e) => set('generalCategory', e.target.value)}>
            <option value="">None</option>{options.categories.map((c) => <option key={c.categoryKey} value={c.categoryKey}>{c.name}</option>)}
          </select>
        </div>
        <div className="col-md-4 d-flex align-items-end"><label className="form-check small"><input type="checkbox" className="form-check-input" checked={f.overflowToGeneral} onChange={(e) => set('overflowToGeneral', e.target.checked)} /><span className="form-check-label">Unused special-category seats go to the general category</span></label></div>
        <div className="col-12">
          <div className="small text-muted mb-1">Seats per category (blank = no limit)</div>
          <div className="row g-2">
            {options.categories.map((c) => (
              <div className="col-6 col-md-3" key={c.categoryKey}><label className="form-label small mb-1">{c.name}</label><input type="number" min="0" className="form-control form-control-sm" value={quota(c.categoryKey)} onChange={(e) => setQuota(c.categoryKey, e.target.value)} /></div>
            ))}
          </div>
        </div>
      </div>
      <div className="card-footer d-flex gap-2 justify-content-end">
        <button type="button" className="btn btn-light btn-sm" onClick={onCancel}>Cancel</button>
        <button className="btn btn-primary btn-sm" disabled={saving}>{saving ? 'Saving…' : 'Save window'}</button>
      </div>
    </form>
  );
}

export default function TransferWindows() {
  const { data: scope } = useOrgScope();
  const [options, setOptions] = useState(null);
  const [list, setList] = useState(null);
  const [edit, setEdit] = useState(null);     // null | form initial
  const [openId, setOpenId] = useState(null);
  const [detail, setDetail] = useState(null);
  const [busy, setBusy] = useState(false);

  const reload = () => listWindows().then(setList).catch((e) => Swal.fire('Unable to load', errMsg(e), 'error'));
  useEffect(() => { reload(); getWindowOptions().then(setOptions).catch((e) => Swal.fire('Unable to load', errMsg(e), 'error')); }, []);

  const loadDetail = (id) => getWindow(id).then(setDetail).catch((e) => Swal.fire('Unable to load', errMsg(e), 'error'));
  useEffect(() => { if (openId) loadDetail(openId); else setDetail(null); }, [openId]); // eslint-disable-line react-hooks/exhaustive-deps

  async function act(id, action, confirmText) {
    if (confirmText) { const r = await Swal.fire({ title: confirmText, icon: 'question', showCancelButton: true, confirmButtonText: 'Yes' }); if (!r.isConfirmed) return; }
    setBusy(true);
    try { const r = await windowAction(id, action); Swal.fire({ icon: 'success', title: r.message, timer: 1800, showConfirmButton: false }); await reload(); if (openId) await loadDetail(openId); }
    catch (e) { Swal.fire('Unable to continue', errMsg(e), 'error'); } finally { setBusy(false); }
  }

  async function resolve(o) {
    const r = await Swal.fire({ title: 'Decision on this objection', input: 'textarea', showCancelButton: true, confirmButtonText: 'Resolve', inputValidator: (v) => (!v || v.trim().length < 3 ? 'Write the decision' : undefined) });
    if (!r.isConfirmed) return;
    try { await resolveObjection(o.id, r.value); await loadDetail(openId); } catch (e) { Swal.fire('Unable to resolve', errMsg(e), 'error'); }
  }

  const toForm = (w, quotas) => ({ id: w.id, name: w.name, typeKey: w.typeKey, scopeNodeId: w.scopeNodeId || '', applyFrom: w.applyFrom || '', applyTo: w.applyTo || '', objectionFrom: w.objectionFrom || '', objectionTo: w.objectionTo || '', ordersFrom: w.ordersFrom || '', effectiveOn: w.effectiveOn || '', totalSeats: w.totalSeats ?? '', generalCategory: w.generalCategory || '', overflowToGeneral: w.overflowToGeneral, quotas: quotas || [] });

  async function editWindow(id) { const x = await getWindow(id); setEdit(toForm(x.window, x.quotas)); }

  if (!list || !options || !scope) return <div className="p-4 text-center"><span className="spinner-border spinner-border-sm" /></div>;
  if (!options.policy) return <div className="container py-4"><div className="alert alert-warning">No transfer policy is active yet.</div></div>;

  if (openId && detail) {
    const w = detail.window;
    const nSel = detail.items.filter((x) => x.sel === 'SELECTED').length;
    return (
      <div className="container-fluid py-3">
        <div className="d-flex flex-wrap align-items-center gap-2 mb-3">
          <button className="btn btn-outline-secondary btn-sm" onClick={() => setOpenId(null)}><i className="bi bi-arrow-left me-1" />Windows</button>
          <h5 className="mb-0">{w.name}</h5><StatusBadge status={w.status} /><span className="text-muted small">{phase(w)}</span>
          <div className="ms-auto d-flex flex-wrap gap-2">
            {w.status === 'DRAFT' && <button className="btn btn-success btn-sm" disabled={busy} onClick={() => act(w.id, 'publish', 'Publish this window?')}>Publish</button>}
            {['PUBLISHED', 'RANKED', 'LIST_PUBLISHED'].includes(w.status) && <button className="btn btn-primary btn-sm" disabled={busy} onClick={() => act(w.id, 'rank', 'Run the ranking now?')}>Run ranking</button>}
            {['RANKED', 'LIST_PUBLISHED'].includes(w.status) && <button className="btn btn-outline-primary btn-sm" disabled={busy} onClick={() => act(w.id, 'publish-list', 'Publish the list to applicants?')}>Publish list</button>}
            {w.status === 'LIST_PUBLISHED' && <button className="btn btn-danger btn-sm" disabled={busy} onClick={() => act(w.id, 'finalize', 'Finalize? Selected applicants are approved for orders and the rest are closed.')}>Finalize</button>}
          </div>
        </div>

        <div className="card mb-3"><div className="card-body row g-2 small">
          <div className="col-md-4"><b>Applications:</b> {fmtDateTime(w.applyFrom)} → {fmtDateTime(w.applyTo)}</div>
          <div className="col-md-4"><b>Objections:</b> {w.objectionFrom ? `${fmtDateTime(w.objectionFrom)} → ${fmtDateTime(w.objectionTo)}` : '—'}</div>
          <div className="col-md-4"><b>Orders from:</b> {fmtDateTime(w.ordersFrom)}</div>
          <div className="col-md-4"><b>Seats:</b> {w.totalSeats ?? 'no limit'}</div>
          <div className="col-md-8"><b>Per category:</b> {detail.quotas.length ? detail.quotas.map((q) => `${q.categoryKey}: ${q.maxSeats}`).join(' · ') : 'no limits'}</div>
        </div></div>

        <div className="card mb-3">
          <div className="card-header fw-semibold">Applications <span className="badge text-bg-light">{detail.items.length}</span> <span className="text-muted small ms-2">{nSel} selected</span></div>
          <div className="table-responsive">
            <table className="table table-sm align-middle mb-0">
              <thead className="table-light"><tr><th>Rank</th><th>Request</th><th>Employee</th><th>Category</th><th>From</th><th>To</th><th>Status</th><th>Result</th></tr></thead>
              <tbody>
                {detail.items.map((r) => (
                  <tr key={r.id}>
                    <td>{r.rankNo ?? '—'}</td><td><Link to={`/transfers/${r.id}`}>{r.requestNo}</Link></td><td>{r.employeeCode} · {r.employeeName}</td><td>{r.categoryKey || '—'}</td><td>{r.fromName}</td><td>{r.toName}</td>
                    <td><StatusBadge status={r.status} /></td>
                    <td>{r.sel ? <span className={`badge ${r.sel === 'SELECTED' ? 'text-bg-success' : 'text-bg-warning'}`}>{r.sel === 'SELECTED' ? 'Selected' : 'Waiting list'}</span> : '—'}{r.note && <div className="small text-muted">{r.note}</div>}</td>
                  </tr>
                ))}
                {detail.items.length === 0 && <tr><td colSpan={8} className="text-center text-muted py-3">No applications visible to you yet.</td></tr>}
              </tbody>
            </table>
          </div>
        </div>

        <div className="card">
          <div className="card-header fw-semibold">Objections <span className="badge text-bg-light">{detail.objections.length}</span></div>
          <ul className="list-group list-group-flush">
            {detail.objections.map((o) => (
              <li key={o.id} className="list-group-item">
                <div className="d-flex gap-2 align-items-start">
                  <div className="flex-grow-1"><b>{o.requestNo}</b> · {o.byName}<div style={{ whiteSpace: 'pre-wrap' }}>{o.text}</div>{o.resolution && <div className="small text-success mt-1">Decision: {o.resolution}</div>}</div>
                  {o.status === 'OPEN' ? <button className="btn btn-sm btn-outline-primary" onClick={() => resolve(o)}>Decide</button> : <span className="badge text-bg-success">Resolved</span>}
                </div>
              </li>
            ))}
            {detail.objections.length === 0 && <li className="list-group-item text-muted">No objections.</li>}
          </ul>
        </div>
      </div>
    );
  }

  return (
    <div className="container-fluid py-3">
      <div className="d-flex align-items-center mb-3 gap-2">
        <h5 className="mb-0">Transfer windows</h5>
        <button className="btn btn-primary btn-sm ms-auto" onClick={() => setEdit({ ...blank })}><i className="bi bi-plus-lg me-1" />New window</button>
      </div>
      {options.types.length === 0 && <div className="alert alert-warning small">The active policy has no window-type transfer. Add one in Transfer Policy and finalize a new version.</div>}
      {edit && <WindowForm key={edit.id || 'new'} initial={edit} options={options} scope={scope} onDone={() => { setEdit(null); reload(); }} onCancel={() => setEdit(null)} />}

      <div className="card">
        <div className="table-responsive">
          <table className="table table-hover align-middle mb-0">
            <thead className="table-light"><tr><th>Window</th><th>Applications</th><th>Phase</th><th>Status</th><th>Requests</th><th /></tr></thead>
            <tbody>
              {list.map((w) => (
                <tr key={w.id}>
                  <td>{w.name}</td><td className="small">{fmtDateTime(w.applyFrom)} → {fmtDateTime(w.applyTo)}</td><td className="small">{phase(w)}</td><td><StatusBadge status={w.status} /></td><td>{w.requests}</td>
                  <td className="text-end text-nowrap">
                    <button className="btn btn-sm btn-outline-primary me-1" onClick={() => setOpenId(w.id)}>Open</button>
                    {['DRAFT', 'PUBLISHED'].includes(w.status) && <button className="btn btn-sm btn-outline-secondary me-1" onClick={() => editWindow(w.id)}><i className="bi bi-pencil" /></button>}
                    {w.status === 'DRAFT' && <button className="btn btn-sm btn-outline-danger" onClick={async () => { const r = await Swal.fire({ title: 'Delete this draft window?', icon: 'warning', showCancelButton: true }); if (r.isConfirmed) { try { await deleteWindow(w.id); reload(); } catch (e) { Swal.fire('Unable to delete', errMsg(e), 'error'); } } }}><i className="bi bi-trash" /></button>}
                  </td>
                </tr>
              ))}
              {list.length === 0 && <tr><td colSpan={6} className="text-center text-muted py-4">No windows yet.</td></tr>}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}