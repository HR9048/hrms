import { useEffect, useState } from 'react';
import Swal from 'sweetalert2';
import { listPolicies, getPolicy, createPolicy, updatePolicy, deletePolicy, clonePolicy, finalizePolicy, savePolicyPart } from '../../api/transfers';
import { listEmployees } from '../../api/employeeMaster';
import { errMsg, fmtDate } from './TransferBits';

const MODES = [['INDIVIDUAL', 'Single request'], ['MUTUAL', 'Mutual exchange (two employees)'], ['WINDOW', 'Window (application period, ranking)']];
const INITIATORS = [['MANAGEMENT', 'Management'], ['EMPLOYEE', 'Employee'], ['BOTH', 'Both']];
const VACANCY = [['NONE', 'Not checked'], ['WARN', 'Warn'], ['RELAX', 'Needs relaxation'], ['BLOCK', 'Block']];
const CHANGE = [['NONE', 'Not allowed'], ['OPTIONAL', 'Optional'], ['REQUIRED', 'Required']];
const SENIORITY = [['RETAIN', 'Retained'], ['BOTTOM', 'Bottom of grade'], ['LOCAL', 'Local seniority']];
const APPLY = [['JOINING', 'When the employee joins'], ['RELIEVING', 'When the employee is relieved']];
const STRENGTH = [['WARN', 'Warn'], ['RELAX', 'Needs relaxation'], ['BLOCK', 'Block']];
const ACTIONS = [['RECOMMEND', 'Recommend'], ['NOC', 'No-objection'], ['APPROVE', 'Approve (final)']];
const ANCHORS = [['EITHER', 'Covers the current or the receiving office'], ['FROM', 'Covers the current office'], ['TO', 'Covers the receiving office']];
const KINDS = {
  MIN_TENURE: { label: 'Minimum stay at the current place', defaults: { default: 3, byGroup: {} } },
  MIN_SERVICE: { label: 'Minimum service', defaults: { years: 5 } },
  PROBATION: { label: 'Probation period', defaults: { months: 12 } },
  NEAR_RETIREMENT: { label: 'Close to retirement', defaults: { retirementAge: 60, months: 12 } },
  FREQUENCY: { label: 'Limit on repeated transfers', defaults: { maxTransfers: 1, years: 3 } },
  MUTUAL_MATCH: { label: 'Mutual transfer: same grade / designation', defaults: { sameGroup: 1, sameDesignation: 1 } }
};
const PARAM_LABELS = { default: 'Default years', years: 'Years', months: 'Months', retirementAge: 'Retirement age', maxTransfers: 'Maximum transfers' };

const Select = ({ value, onChange, options, disabled }) => (
  <select className="form-select form-select-sm" value={value} disabled={disabled} onChange={(e) => onChange(e.target.value)}>
    {options.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
  </select>
);
const Check = ({ checked, onChange, disabled, label }) => (
  <label className="form-check form-check-inline small mb-0">
    <input type="checkbox" className="form-check-input" checked={!!checked} disabled={disabled} onChange={(e) => onChange(e.target.checked)} />
    <span className="form-check-label">{label}</span>
  </label>
);
const F = ({ label, children, cls = 'col-md-3' }) => <div className={cls}><label className="form-label small mb-1">{label}</label>{children}</div>;
const toggle = (arr, v) => (arr.includes(v) ? arr.filter((x) => x !== v) : [...arr, v]);

function ParamEditor({ rule, onChange, disabled }) {
  const p = rule.params || {};
  const groups = Object.entries(p.byGroup || {});
  const setGroups = (next) => onChange({ ...p, byGroup: Object.fromEntries(next.filter(([k]) => k !== '')) });

  if (rule.ruleKind === 'MUTUAL_MATCH') {
    return (
      <div>
        <Check disabled={disabled} label="Same employee group" checked={p.sameGroup === 1} onChange={(on) => onChange({ ...p, sameGroup: on ? 1 : 0 })} />
        <Check disabled={disabled} label="Same designation" checked={p.sameDesignation === 1} onChange={(on) => onChange({ ...p, sameDesignation: on ? 1 : 0 })} />
      </div>
    );
  }
  const nums = Object.keys(KINDS[rule.ruleKind]?.defaults || {}).filter((k) => k !== 'byGroup');
  return (
    <div className="row g-2">
      {nums.map((k) => (
        <F key={k} cls="col-6 col-md-3" label={PARAM_LABELS[k] || k}>
          <input type="number" min="0" step="0.5" className="form-control form-control-sm" disabled={disabled} value={p[k] ?? ''} onChange={(e) => onChange({ ...p, [k]: e.target.value === '' ? '' : Number(e.target.value) })} />
        </F>
      ))}
      {rule.ruleKind === 'MIN_TENURE' && (
        <div className="col-12">
          <label className="form-label small mb-1">Different years for specific employee groups (the group value stored in the employee master)</label>
          {groups.map(([k, v], i) => (
            <div className="d-flex gap-2 mb-1" key={i}>
              <input className="form-control form-control-sm" style={{ maxWidth: 160 }} placeholder="Group value" disabled={disabled} value={k} onChange={(e) => setGroups(groups.map((g, gi) => (gi === i ? [e.target.value, g[1]] : g)))} />
              <input type="number" min="0" step="0.5" className="form-control form-control-sm" style={{ maxWidth: 110 }} placeholder="Years" disabled={disabled} value={v} onChange={(e) => setGroups(groups.map((g, gi) => (gi === i ? [g[0], e.target.value === '' ? '' : Number(e.target.value)] : g)))} />
              {!disabled && <button type="button" className="btn btn-sm btn-outline-danger" onClick={() => setGroups(groups.filter((_, gi) => gi !== i))}><i className="bi bi-x" /></button>}
            </div>
          ))}
          {!disabled && <button type="button" className="btn btn-sm btn-link p-0" onClick={() => setGroups([...groups, ['', 3]])}>+ Add a group</button>}
        </div>
      )}
    </div>
  );
}

// choose specific employees as the authority of a step
function PeoplePicker({ ids, names, onChange, disabled }) {
  const [q, setQ] = useState('');
  const [found, setFound] = useState([]);
  useEffect(() => {
    if (q.trim().length < 2) { setFound([]); return undefined; }
    const t = setTimeout(() => listEmployees({ search: q.trim(), pageSize: 6 }).then((d) => setFound(d.items || [])).catch(() => setFound([])), 350);
    return () => clearTimeout(t);
  }, [q]);

  return (
    <div>
      <div className="d-flex flex-wrap gap-1 mb-1">
        {ids.map((id) => (
          <span key={id} className="badge text-bg-primary d-flex align-items-center gap-1">
            {names[id] || `#${id}`}
            {!disabled && <i role="button" className="bi bi-x" onClick={() => onChange(ids.filter((x) => x !== id))} />}
          </span>
        ))}
        {ids.length === 0 && <span className="text-muted small">No employee chosen</span>}
      </div>
      {!disabled && (
        <>
          <input className="form-control form-control-sm" placeholder="Search by employee code or name to add…" value={q} onChange={(e) => setQ(e.target.value)} />
          {found.length > 0 && (
            <div className="list-group mt-1">
              {found.map((x) => (
                <button type="button" key={x.employeeId} className="list-group-item list-group-item-action py-1 small"
                  onClick={() => { onChange(ids.includes(x.employeeId) ? ids : [...ids, x.employeeId]); names[x.employeeId] = `${x.employeeCode} · ${x.firstName} ${x.lastName || ''}`.trim(); setQ(''); setFound([]); }}>
                  <b>{x.employeeCode}</b> {x.firstName} {x.lastName}
                </button>
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}

function PolicyEditor({ id, onBack }) {
  const [d, setD] = useState(null);
  const [tab, setTab] = useState('types');
  const [types, setTypes] = useState([]);
  const [cats, setCats] = useState([]);
  const [rules, setRules] = useState([]);
  const [steps, setSteps] = useState({});
  const [rot, setRot] = useState([]);
  const [fmap, setFmap] = useState({});
  const [names, setNames] = useState({});
  const [busy, setBusy] = useState(false);

  async function load() {
    const x = await getPolicy(id);
    setD(x); setTypes(x.types); setCats(x.categories); setRules(x.rules); setRot(x.rotation);
    const g = {};
    x.types.forEach((t) => { g[t.typeKey] = []; });
    x.steps.forEach((s) => { (g[s.typeKey] ||= []).push(s); });
    setSteps(g);
    const m = {};
    x.fieldMap.forEach((f) => { m[f.inputKey] = `${f.tableName}|${f.columnName}`; });
    setFmap(m);
    const n = {};
    x.people.forEach((p) => { n[p.id] = p.label; });
    setNames(n);
  }
  useEffect(() => { load().catch((e) => Swal.fire('Unable to load', errMsg(e), 'error')); }, [id]); // eslint-disable-line react-hooks/exhaustive-deps

  if (!d) return <div className="p-4 text-center"><span className="spinner-border spinner-border-sm" /></div>;

  const draft = d.policy.status === 'DRAFT';
  const typeKeys = d.types.map((t) => t.typeKey);

  async function save(part, rows) {
    setBusy(true);
    try { await savePolicyPart(id, part, rows); await load(); Swal.fire({ icon: 'success', title: 'Saved', timer: 900, showConfirmButton: false }); }
    catch (e) { Swal.fire('Unable to save', errMsg(e), 'error'); }
    finally { setBusy(false); }
  }

  async function finalize() {
    const r = await Swal.fire({ title: 'Finalize this policy?', html: 'It becomes the active policy and <b>can no longer be edited</b>. To change rules later you create a new version. Requests in progress keep the version they started under.', icon: 'warning', showCancelButton: true, confirmButtonText: 'Finalize' });
    if (!r.isConfirmed) return;
    try { await finalizePolicy(id); await load(); } catch (e) { Swal.fire('Cannot finalize', errMsg(e), 'error'); }
  }

  async function newVersion() {
    try { const x = await clonePolicy(id); onBack(x.id); } catch (e) { Swal.fire('Unable to create', errMsg(e), 'error'); }
  }

  const upd = (setter) => (i, patch) => setter((prev) => prev.map((r, idx) => (idx === i ? { ...r, ...patch } : r)));
  const updType = upd(setTypes); const updCat = upd(setCats); const updRule = upd(setRules); const updRot = upd(setRot);
  const tabs = [['types', 'Types'], ['categories', 'Reason categories'], ['rules', 'Rules'], ['steps', 'Workflow'], ['rotation', 'Rotation'], ['fieldmap', 'Field mapping']];

  return (
    <div className="container-fluid py-3">
      <div className="d-flex flex-wrap align-items-center gap-2 mb-3">
        <button className="btn btn-outline-secondary btn-sm" onClick={() => onBack()}><i className="bi bi-arrow-left me-1" />Policies</button>
        <h5 className="mb-0">{d.policy.name} <span className="text-muted">v{d.policy.version}</span></h5>
        <span className={`badge ${draft ? 'text-bg-warning' : d.policy.status === 'ACTIVE' ? 'text-bg-success' : 'text-bg-secondary'}`}>{d.policy.status}</span>
        <div className="ms-auto d-flex gap-2">
          {draft && <button className="btn btn-danger btn-sm" onClick={finalize}><i className="bi bi-lock me-1" />Finalize</button>}
          {!draft && <button className="btn btn-primary btn-sm" onClick={newVersion}><i className="bi bi-files me-1" />Create new version</button>}
        </div>
      </div>
      {!draft ? <div className="alert alert-secondary py-2 small">This version is finalized and read-only.</div>
        : <div className="alert alert-info py-2 small">Save each tab after editing it. Rules start as <b>Warn</b>, so nothing blocks a request until the corporation confirms its real rules.</div>}

      <ul className="nav nav-tabs mb-3 flex-nowrap overflow-auto">
        {tabs.map(([k, l]) => <li className="nav-item" key={k}><button className={`nav-link text-nowrap ${tab === k ? 'active' : ''}`} onClick={() => setTab(k)}>{l}</button></li>)}
      </ul>

      {tab === 'types' && (
        <>
          {types.map((t, i) => (
            <div className="card mb-2" key={i}>
              <div className="card-body">
                <div className="row g-2">
                  <F cls="col-md-2" label="Key"><input className="form-control form-control-sm font-monospace" disabled={!draft} value={t.typeKey} onChange={(e) => updType(i, { typeKey: e.target.value.toUpperCase() })} /></F>
                  <F cls="col-md-4" label="Name"><input className="form-control form-control-sm" disabled={!draft} value={t.name} onChange={(e) => updType(i, { name: e.target.value })} /></F>
                  <F cls="col-md-4" label="How it works"><Select disabled={!draft} value={t.procMode} options={MODES} onChange={(v) => updType(i, { procMode: v })} /></F>
                  <F cls="col-md-2" label="Started by"><Select disabled={!draft} value={t.initiatedBy} options={INITIATORS} onChange={(v) => updType(i, { initiatedBy: v })} /></F>
                  <F label="Vacancy at the destination"><Select disabled={!draft} value={t.vacancyCheck} options={VACANCY} onChange={(v) => updType(i, { vacancyCheck: v })} /></F>
                  <F label="Department change"><Select disabled={!draft} value={t.deptChange} options={CHANGE} onChange={(v) => updType(i, { deptChange: v })} /></F>
                  <F label="Designation change"><Select disabled={!draft} value={t.desigChange} options={CHANGE} onChange={(v) => updType(i, { desigChange: v })} /></F>
                  <F label="Effect on seniority"><Select disabled={!draft} value={t.seniorityEffect} options={SENIORITY} onChange={(v) => updType(i, { seniorityEffect: v })} /></F>
                  <F label="Posting changes"><Select disabled={!draft} value={t.applyOn} options={APPLY} onChange={(v) => updType(i, { applyOn: v })} /></F>
                  <div className="col-md-9 d-flex flex-wrap align-items-end gap-3 pb-1">
                    <Check disabled={!draft} label="Employee consent needed" checked={t.needsConsent} onChange={(v) => updType(i, { needsConsent: v })} />
                    <Check disabled={!draft} label="Reason category needed" checked={t.needsCategory} onChange={(v) => updType(i, { needsCategory: v })} />
                    <Check disabled={!draft} label="Type is on" checked={t.isEnabled} onChange={(v) => updType(i, { isEnabled: v })} />
                    {draft && <button className="btn btn-sm btn-outline-danger ms-auto" onClick={() => setTypes(types.filter((_, x) => x !== i))}><i className="bi bi-trash me-1" />Remove</button>}
                  </div>
                </div>
              </div>
            </div>
          ))}
          {draft && (
            <div className="d-flex gap-2">
              <button className="btn btn-outline-primary btn-sm" onClick={() => setTypes([...types, { typeKey: '', name: '', procMode: 'INDIVIDUAL', initiatedBy: 'MANAGEMENT', needsConsent: false, needsCategory: false, vacancyCheck: 'NONE', deptChange: 'NONE', desigChange: 'NONE', seniorityEffect: 'RETAIN', applyOn: 'JOINING', isEnabled: true }])}>+ Add type</button>
              <button className="btn btn-primary btn-sm ms-auto" disabled={busy} onClick={() => save('types', types)}>Save types</button>
            </div>
          )}
        </>
      )}

      {tab === 'categories' && (
        <div className="card">
          <div className="table-responsive">
            <table className="table table-sm align-middle mb-0">
              <thead className="table-light"><tr><th>Key</th><th>Name</th><th style={{ width: 90 }}>Priority</th><th>Required document</th><th>No vacancy needed</th><th>On</th><th /></tr></thead>
              <tbody>
                {cats.map((t, i) => (
                  <tr key={i}>
                    <td style={{ width: 140 }}><input className="form-control form-control-sm font-monospace" disabled={!draft} value={t.categoryKey} onChange={(e) => updCat(i, { categoryKey: e.target.value.toUpperCase() })} /></td>
                    <td style={{ minWidth: 200 }}><input className="form-control form-control-sm" disabled={!draft} value={t.name} onChange={(e) => updCat(i, { name: e.target.value })} /></td>
                    <td><input type="number" min="1" className="form-control form-control-sm" disabled={!draft} value={t.priorityRank} onChange={(e) => updCat(i, { priorityRank: Number(e.target.value) })} /></td>
                    <td style={{ minWidth: 200 }}><input className="form-control form-control-sm" placeholder="None" disabled={!draft} value={t.docLabel || ''} onChange={(e) => updCat(i, { docLabel: e.target.value })} /></td>
                    <td className="text-center"><input type="checkbox" className="form-check-input" disabled={!draft} checked={t.waivesVacancy} onChange={(e) => updCat(i, { waivesVacancy: e.target.checked })} /></td>
                    <td className="text-center"><input type="checkbox" className="form-check-input" disabled={!draft} checked={t.isEnabled} onChange={(e) => updCat(i, { isEnabled: e.target.checked })} /></td>
                    <td>{draft && <button className="btn btn-sm btn-outline-danger" onClick={() => setCats(cats.filter((_, x) => x !== i))}><i className="bi bi-trash" /></button>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {draft && (
            <div className="card-footer d-flex gap-2">
              <button className="btn btn-outline-primary btn-sm" onClick={() => setCats([...cats, { categoryKey: '', name: '', priorityRank: 100, docLabel: '', waivesVacancy: false, isEnabled: true }])}>+ Add category</button>
              <button className="btn btn-primary btn-sm ms-auto" disabled={busy} onClick={() => save('categories', cats)}>Save categories</button>
            </div>
          )}
        </div>
      )}

      {tab === 'rules' && (
        <>
          {rules.map((r, i) => (
            <div className="card mb-3" key={i}>
              <div className="card-body">
                <div className="row g-2 mb-2">
                  <F cls="col-md-2" label="Key"><input className="form-control form-control-sm font-monospace" disabled={!draft} value={r.ruleKey} onChange={(e) => updRule(i, { ruleKey: e.target.value.toUpperCase() })} /></F>
                  <F cls="col-md-4" label="Name"><input className="form-control form-control-sm" disabled={!draft} value={r.name} onChange={(e) => updRule(i, { name: e.target.value })} /></F>
                  <F label="Rule"><Select disabled={!draft} value={r.ruleKind} options={Object.entries(KINDS).map(([k, v]) => [k, v.label])} onChange={(v) => updRule(i, { ruleKind: v, params: structuredClone(KINDS[v].defaults) })} /></F>
                  <F label="If it fails"><Select disabled={!draft} value={r.strength} options={STRENGTH} onChange={(v) => updRule(i, { strength: v })} /></F>
                </div>
                <ParamEditor rule={r} disabled={!draft} onChange={(params) => updRule(i, { params })} />
                <div className="mt-3 small">
                  <div className="mb-1"><b>Applies to:</b>{' '}
                    <Check disabled={!draft} label="All types" checked={r.appliesTypes.includes('*') || r.appliesTypes.length === 0} onChange={(on) => updRule(i, { appliesTypes: on ? ['*'] : [] })} />
                    {!(r.appliesTypes.includes('*') || r.appliesTypes.length === 0) && typeKeys.map((k) => <Check key={k} disabled={!draft} label={k} checked={r.appliesTypes.includes(k)} onChange={() => updRule(i, { appliesTypes: toggle(r.appliesTypes, k) })} />)}
                  </div>
                  <div className="mb-1"><b>Exempt categories:</b>{' '}
                    {d.categories.length === 0 && <span className="text-muted">none defined</span>}
                    {d.categories.map((c) => <Check key={c.categoryKey} disabled={!draft} label={c.categoryKey} checked={r.exemptCategories.includes(c.categoryKey)} onChange={() => updRule(i, { exemptCategories: toggle(r.exemptCategories, c.categoryKey) })} />)}
                  </div>
                  <div className="d-flex flex-wrap align-items-center gap-3">
                    {r.strength === 'RELAX' && (
                      <span><b>Role that may relax it:</b>{' '}
                        <select className="form-select form-select-sm d-inline-block w-auto" disabled={!draft} value={r.relaxRoleCode || ''} onChange={(e) => updRule(i, { relaxRoleCode: e.target.value || null })}>
                          <option value="">Any approver</option>
                          {d.roles.map((x) => <option key={x.code} value={x.code}>{x.name}</option>)}
                        </select>
                      </span>
                    )}
                    <Check disabled={!draft} label="Rule is on" checked={r.isEnabled} onChange={(on) => updRule(i, { isEnabled: on })} />
                    {draft && <button className="btn btn-sm btn-outline-danger ms-auto" onClick={() => setRules(rules.filter((_, x) => x !== i))}><i className="bi bi-trash me-1" />Remove</button>}
                  </div>
                </div>
              </div>
            </div>
          ))}
          {draft && (
            <div className="d-flex gap-2">
              <button className="btn btn-outline-primary btn-sm" onClick={() => setRules([...rules, { ruleKey: '', name: '', ruleKind: 'MIN_TENURE', params: structuredClone(KINDS.MIN_TENURE.defaults), appliesTypes: ['*'], exemptCategories: [], strength: 'WARN', relaxRoleCode: null, isEnabled: true }])}>+ Add rule</button>
              <button className="btn btn-primary btn-sm ms-auto" disabled={busy} onClick={() => save('rules', rules)}>Save rules</button>
            </div>
          )}
        </>
      )}

      {tab === 'steps' && (
        <>
          {d.types.length === 0 && <div className="alert alert-warning">Save at least one transfer type first.</div>}
          {d.types.map((t) => (
            <div className="card mb-3" key={t.typeKey}>
              <div className="card-header fw-semibold">{t.name} <span className="text-muted font-monospace small">{t.typeKey}</span></div>
              <div className="card-body">
                {(steps[t.typeKey] || []).map((s, i) => {
                  const list = steps[t.typeKey];
                  const setStep = (patch) => setSteps({ ...steps, [t.typeKey]: list.map((x, xi) => (xi === i ? { ...x, ...patch } : x)) });
                  const move = (dir) => { const j = i + dir; if (j < 0 || j >= list.length) return; const next = [...list]; [next[i], next[j]] = [next[j], next[i]]; setSteps({ ...steps, [t.typeKey]: next }); };
                  return (
                    <div className="border rounded p-2 mb-2" key={i}>
                      <div className="row g-2 align-items-end">
                        <div className="col-md-1 text-muted fw-bold">Step {i + 1}</div>
                        <F cls="col-md-4" label="Step name"><input className="form-control form-control-sm" disabled={!draft} value={s.name} onChange={(e) => setStep({ name: e.target.value })} /></F>
                        <F cls="col-md-3" label="Action"><Select disabled={!draft} value={s.action} options={ACTIONS} onChange={(v) => setStep({ action: v })} /></F>
                        <F cls="col-md-3" label="Who decides"><Select disabled={!draft} value={s.authority} options={[['ROLE', 'A role'], ['EMPLOYEE', 'Specific employees']]} onChange={(v) => setStep({ authority: v })} /></F>
                        {draft && (
                          <div className="col-md-1 d-flex gap-1">
                            <button className="btn btn-sm btn-outline-secondary" onClick={() => move(-1)}><i className="bi bi-arrow-up" /></button>
                            <button className="btn btn-sm btn-outline-secondary" onClick={() => move(1)}><i className="bi bi-arrow-down" /></button>
                            <button className="btn btn-sm btn-outline-danger" onClick={() => setSteps({ ...steps, [t.typeKey]: list.filter((_, xi) => xi !== i) })}><i className="bi bi-trash" /></button>
                          </div>
                        )}
                      </div>
                      {s.authority === 'ROLE' ? (
                        <div className="mt-2 small">
                          <b>Roles:</b> {d.roles.map((r) => <Check key={r.code} disabled={!draft} label={r.name} checked={s.roleCodes.includes(r.code)} onChange={() => setStep({ roleCodes: toggle(s.roleCodes, r.code) })} />)}
                          <div className="row g-2 mt-1"><F cls="col-md-6" label="Data-flow scope (who may see and act)"><Select disabled={!draft} value={s.anchor} options={ANCHORS} onChange={(v) => setStep({ anchor: v })} /></F></div>
                        </div>
                      ) : (
                        <div className="mt-2 small"><b>Employees:</b><PeoplePicker disabled={!draft} ids={s.employeeIds} names={names} onChange={(ids) => setStep({ employeeIds: ids })} /></div>
                      )}
                    </div>
                  );
                })}
                {draft && <button className="btn btn-sm btn-outline-primary" onClick={() => setSteps({ ...steps, [t.typeKey]: [...(steps[t.typeKey] || []), { name: '', action: 'RECOMMEND', authority: 'ROLE', roleCodes: [], employeeIds: [], anchor: 'EITHER' }] })}>+ Add step</button>}
              </div>
            </div>
          ))}
          {draft && d.types.length > 0 && (
            <div className="d-flex">
              <div className="small text-muted me-3 align-self-center">The last step of every type must be an Approve step. For window types this step verifies eligibility; the committee decides at the window's end.</div>
              <button className="btn btn-primary btn-sm ms-auto" disabled={busy}
                onClick={() => save('steps', d.types.flatMap((t) => (steps[t.typeKey] || []).map((s) => ({ typeKey: t.typeKey, name: s.name, action: s.action, authority: s.authority, roleCodes: s.roleCodes, employeeIds: s.employeeIds, anchor: s.anchor }))))}>
                Save workflow
              </button>
            </div>
          )}
        </>
      )}

      {tab === 'rotation' && (
        <>
          <p className="small text-muted">Rotation rules produce the <b>due list</b>: employees who have stayed at one place longer than the maximum. Choose designations to limit a rule to sensitive posts, or leave none for everyone.</p>
          {rot.map((r, i) => (
            <div className="card mb-2" key={i}>
              <div className="card-body">
                <div className="row g-2">
                  <F cls="col-md-2" label="Key"><input className="form-control form-control-sm font-monospace" disabled={!draft} value={r.ruleKey} onChange={(e) => updRot(i, { ruleKey: e.target.value.toUpperCase() })} /></F>
                  <F cls="col-md-5" label="Name"><input className="form-control form-control-sm" disabled={!draft} value={r.name} onChange={(e) => updRot(i, { name: e.target.value })} /></F>
                  <F cls="col-md-2" label="Maximum years"><input type="number" min="0.5" step="0.5" className="form-control form-control-sm" disabled={!draft} value={r.maxYears} onChange={(e) => updRot(i, { maxYears: Number(e.target.value) })} /></F>
                  <F cls="col-md-3" label="Show in the list this many months early"><input type="number" min="0" className="form-control form-control-sm" disabled={!draft} value={r.warnMonths} onChange={(e) => updRot(i, { warnMonths: Number(e.target.value) })} /></F>
                  <div className="col-12 small"><b>Designations:</b>{' '}
                    {d.designations.map((x) => <Check key={x.id} disabled={!draft} label={x.name} checked={r.designationIds.includes(x.id)} onChange={() => updRot(i, { designationIds: toggle(r.designationIds, x.id) })} />)}
                  </div>
                  <div className="col-12 d-flex gap-3">
                    <Check disabled={!draft} label="Rule is on" checked={r.isEnabled} onChange={(v) => updRot(i, { isEnabled: v })} />
                    {draft && <button className="btn btn-sm btn-outline-danger ms-auto" onClick={() => setRot(rot.filter((_, x) => x !== i))}><i className="bi bi-trash me-1" />Remove</button>}
                  </div>
                </div>
              </div>
            </div>
          ))}
          {draft && (
            <div className="d-flex gap-2">
              <button className="btn btn-outline-primary btn-sm" onClick={() => setRot([...rot, { ruleKey: '', name: '', designationIds: [], maxYears: 5, warnMonths: 6, isEnabled: true }])}>+ Add rotation rule</button>
              <button className="btn btn-primary btn-sm ms-auto" disabled={busy} onClick={() => save('rotation', rot)}>Save rotation rules</button>
            </div>
          )}
        </>
      )}

      {tab === 'fieldmap' && (
        <div className="card">
          <div className="card-body">
            <p className="small text-muted">Choose which employee-master fields hold what the rules need. Only fields in finalized sections are listed. If a field is missing, add a section such as "Service Details" in Employee Master Setup first. A rule whose input is not mapped shows "Not checked", and window ranking uses the date of joining as seniority.</p>
            {[['JOIN_DATE', 'Date of joining', ['DATE']], ['DOB', 'Date of birth', ['DATE']], ['GROUP', 'Employee group / grade', ['DROPDOWN', 'TEXT']]].map(([key, label, allowed]) => (
              <div className="row g-2 mb-2 align-items-center" key={key}>
                <div className="col-md-3 fw-semibold">{label}</div>
                <div className="col-md-6">
                  <select className="form-select form-select-sm" disabled={!draft} value={fmap[key] || ''} onChange={(e) => setFmap({ ...fmap, [key]: e.target.value })}>
                    <option value="">Not mapped</option>
                    {d.fieldOptions.filter((f) => allowed.includes(f.dataType)).map((f) => <option key={`${f.tableName}|${f.columnName}`} value={`${f.tableName}|${f.columnName}`}>{f.section} › {f.label}</option>)}
                  </select>
                </div>
              </div>
            ))}
          </div>
          {draft && (
            <div className="card-footer text-end">
              <button className="btn btn-primary btn-sm" disabled={busy} onClick={() => save('fieldmap', Object.entries(fmap).filter(([, v]) => v).map(([k, v]) => ({ inputKey: k, tableName: v.split('|')[0], columnName: v.split('|')[1] })))}>Save mapping</button>
            </div>
          )}
        </div>
      )}

      {draft && (
        <div className="mt-3">
          <label className="form-label small">Notes for this version</label>
          <div className="d-flex gap-2">
            <input className="form-control form-control-sm" defaultValue={d.policy.notes || ''} id="tr-notes" placeholder="For example: based on the corporation's circular dated …" />
            <button className="btn btn-outline-secondary btn-sm" onClick={() => updatePolicy(id, { notes: document.getElementById('tr-notes').value, effectiveFrom: d.policy.effectiveFrom }).then(() => Swal.fire({ icon: 'success', title: 'Saved', timer: 900, showConfirmButton: false })).catch((e) => Swal.fire('Unable to save', errMsg(e), 'error'))}>Save notes</button>
          </div>
        </div>
      )}
    </div>
  );
}

export default function TransferPolicy() {
  const [list, setList] = useState(null);
  const [openId, setOpenId] = useState(null);
  const [name, setName] = useState('');
  const [preset, setPreset] = useState('starter');

  const reload = () => listPolicies().then(setList).catch((e) => Swal.fire('Unable to load', errMsg(e), 'error'));
  useEffect(() => { reload(); }, []);

  async function create(e) {
    e.preventDefault();
    try { const r = await createPolicy({ name, preset }); setName(''); await reload(); setOpenId(r.id); } catch (err) { Swal.fire('Unable to create', errMsg(err), 'error'); }
  }
  async function remove(p) {
    const r = await Swal.fire({ title: `Delete draft "${p.name}" v${p.version}?`, icon: 'warning', showCancelButton: true, confirmButtonText: 'Delete', confirmButtonColor: '#dc3545' });
    if (!r.isConfirmed) return;
    try { await deletePolicy(p.id); reload(); } catch (err) { Swal.fire('Unable to delete', errMsg(err), 'error'); }
  }

  if (openId) return <PolicyEditor id={openId} onBack={(next) => { setOpenId(next || null); reload(); }} />;
  if (!list) return <div className="p-4 text-center"><span className="spinner-border spinner-border-sm" /></div>;

  return (
    <div className="container-fluid py-3" style={{ maxWidth: 1000 }}>
      <h5 className="mb-3">Transfer policy</h5>
      <form className="card mb-3" onSubmit={create}>
        <div className="card-body row g-2 align-items-end">
          <F cls="col-md-5" label="New policy name"><input className="form-control form-control-sm" required value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Corporation Transfer Policy" /></F>
          <F cls="col-md-4" label="Start from"><Select value={preset} options={[['starter', 'Starter (all transfer types, rules as warnings)'], ['', 'Blank']]} onChange={setPreset} /></F>
          <div className="col-md-3"><button className="btn btn-primary btn-sm w-100">Create draft</button></div>
        </div>
      </form>
      <div className="card">
        <div className="table-responsive">
          <table className="table align-middle mb-0">
            <thead className="table-light"><tr><th>Name</th><th>Version</th><th>Status</th><th>Effective from</th><th>Requests</th><th /></tr></thead>
            <tbody>
              {list.map((p) => (
                <tr key={p.id}>
                  <td>{p.name}</td><td>v{p.version}</td>
                  <td><span className={`badge ${p.status === 'ACTIVE' ? 'text-bg-success' : p.status === 'DRAFT' ? 'text-bg-warning' : 'text-bg-secondary'}`}>{p.status}</span></td>
                  <td>{fmtDate(p.effectiveFrom)}</td><td>{p.requests}</td>
                  <td className="text-end text-nowrap">
                    <button className="btn btn-sm btn-outline-primary me-1" onClick={() => setOpenId(p.id)}>{p.status === 'DRAFT' ? 'Edit' : 'View'}</button>
                    {p.status === 'DRAFT' && <button className="btn btn-sm btn-outline-danger" onClick={() => remove(p)}><i className="bi bi-trash" /></button>}
                  </td>
                </tr>
              ))}
              {list.length === 0 && <tr><td colSpan={6} className="text-center text-muted py-4">No policy yet. Create one above.</td></tr>}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}