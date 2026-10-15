import { useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import Swal from 'sweetalert2';
import DynamicField, { validateField, ymd } from '../../components/DynamicField';
import OrgNodePicker from '../../components/OrgNodePicker';
import EmployeeAccounts from '../../components/EmployeeAccounts';
import { getMaster, getLookups, getEmployee, getEmployeeHistory, createEmployee, updateEmployee } from '../../api/employeeMaster';
import { uploadFile, deleteFile } from '../../api/files';
import usePageAccess from '../../hooks/usePageAccess';
import { DEFAULT_PASSWORD, MIN_PASSWORD, isEmployeeRole } from '../../utils/accountDefaults';

const LIST_PATH = '/employee/All'; // adjust to your list route
const errMsg = (e) => e?.response?.data?.message || e?.message || 'Something went wrong.';
// the first row defaults to the Employee role; extra rows start without a role
const makeAccount = (roles, first = false) => ({
  roleId: first ? (roles.find(isEmployeeRole)?.id ?? '') : '',
  username: '',
  password: DEFAULT_PASSWORD
});
// the pre-filled default password alone doesn't make a row "filled in"
const touched = (a) => a.roleId || a.username.trim();

function emptyValues(sections) {
  const out = {};
  sections.forEach((s) => {
    out[s.key] = {};
    s.fields.forEach((f) => {
      out[s.key][f.columnName] = f.dataType === 'BOOLEAN' ? false : f.columnName === 'status' ? 'ACTIVE' : '';
    });
  });
  return out;
}

function fromServer(sections, saved) {
  const out = emptyValues(sections);
  sections.forEach((s) => s.fields.forEach((f) => {
    const v = saved?.[s.key]?.[f.columnName];
    out[s.key][f.columnName] = f.dataType === 'FILE' ? v || '' : v ?? (f.dataType === 'BOOLEAN' ? false : '');
  }));
  return out;
}

export default function EmployeeCreate1() {
  const { id } = useParams();
  const editingId = id ? Number(id) : null;
  const navigate = useNavigate();
  const location = useLocation();
  const backTo = location.state?.from || LIST_PATH;

  const [master, setMaster] = useState(null);
  const [lookups, setLookups] = useState(null);
  const [values, setValues] = useState({});
  const [accounts, setAccounts] = useState([]);
  const [orgNodeId, setOrgNodeId] = useState(null);
  const [origNodeId, setOrigNodeId] = useState(null);
  const [origEffective, setOrigEffective] = useState(null);
  const [effectiveFrom, setEffectiveFrom] = useState(ymd(new Date()));
  const [errors, setErrors] = useState({});
  const [version, setVersion] = useState(null);
  const [loading, setLoading] = useState(true);
  const [notFound, setNotFound] = useState(false);
  const [saving, setSaving] = useState(false);
  const [uploading, setUploading] = useState(null); // { current, total, name }
  const [formKey, setFormKey] = useState(0);
  const [history, setHistory] = useState(null);
  const { canView, canAdd, canEdit, canDelete } = usePageAccess();
  // files uploaded during a submit that has not (yet) saved - reused on retry, cleaned up when the page is left
  const uploadedRef = useRef({});

  const sections = useMemo(() => (master?.sections || []).filter((s) => s.isFinalized), [master]);
  const basicReady = master?.sections.some((s) => s.key === 'basic' && s.isFinalized);
  const codeMode = master?.config.codeMode;
  const canManageAccounts = !!lookups?.me?.canManageAccounts;

  useEffect(() => () => {
    Object.values(uploadedRef.current).forEach((u) => deleteFile('secure', u.fileId).catch(() => { }));
    uploadedRef.current = {};
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      setLoading(true);
      setNotFound(false);
      setHistory(null);
      try {
        const [m, l] = await Promise.all([getMaster(), getLookups()]);
        if (cancelled) return;
        setMaster(m);
        setLookups(l);
        const fin = m.sections.filter((s) => s.isFinalized);

        if (editingId) {
          const res = await getEmployee(editingId);
          if (cancelled) return;
          setValues(fromServer(fin, res.sections));
          setVersion(res.version);
          setOrgNodeId(res.organization?.nodeId ?? null);
          setOrigNodeId(res.organization?.nodeId ?? null);
          setOrigEffective(res.organization?.effectiveFrom ?? null);
        } else {
          setValues(emptyValues(fin));
          setVersion(null);
          setOrgNodeId(null);
          setOrigNodeId(null);
          setOrigEffective(null);
        }
        setAccounts([makeAccount(l.roles, true)]);
        setEffectiveFrom(ymd(new Date()));
        setErrors({});
        setFormKey((k) => k + 1);
      } catch (e) {
        if (!cancelled) {
          const st = e?.response?.status;
          if (st === 404 || st === 403) setNotFound(true);
          else Swal.fire('Unable to load', errMsg(e), 'error');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [id]); // eslint-disable-line react-hooks/exhaustive-deps

  const skipCode = (s, f) => s.key === 'basic' && f.columnName === 'employee_code' && (editingId || codeMode === 'AUTO');
  const orgChanged = editingId ? orgNodeId !== origNodeId : true;

  const setField = (sKey, col, v) => {
    setValues((p) => ({ ...p, [sKey]: { ...p[sKey], [col]: v } }));
    setErrors((p) => ({ ...p, [`${sKey}.${col}`]: undefined }));
  };

  const setAccount = (i, key, v) => {
    setAccounts((p) => p.map((a, idx) => (idx === i ? { ...a, [key]: v } : a)));
    setErrors((p) => ({ ...p, [`accounts.${i}.${key}`]: undefined }));
  };

  // one by one; a file already uploaded for the same selection is reused (retry after a failed save)
  async function uploadPending() {
    const jobs = [];
    sections.forEach((s) => s.fields.forEach((f) => {
      const v = values[s.key]?.[f.columnName];
      if (f.dataType === 'FILE' && v?.file) jobs.push({ key: `${s.key}.${f.columnName}`, file: v.file });
    }));

    for (const [k, u] of Object.entries(uploadedRef.current)) {
      if (!jobs.some((j) => j.key === k && j.file === u.file)) {
        deleteFile('secure', u.fileId).catch(() => { });
        delete uploadedRef.current[k];
      }
    }

    let n = 0;
    for (const j of jobs) {
      n += 1;
      if (uploadedRef.current[j.key]) continue;
      setUploading({ current: n, total: jobs.length, name: j.file.name });
      try {
        const res = await uploadFile(j.file, { type: 'secure', category: 'employee-docs' });
        uploadedRef.current[j.key] = { file: j.file, fileId: res.fileId, fileName: res.originalFileName || j.file.name };
      } catch (err) {
        throw new Error(`Could not upload "${j.file.name}": ${errMsg(err)}`);
      }
    }
    setUploading(null);
  }

  async function submit(e, addAnother = false) {
    e.preventDefault();

    const clientErrors = {};
    sections.forEach((s) => s.fields.forEach((f) => {
      if (skipCode(s, f)) return;
      const err = validateField(f, values[s.key]?.[f.columnName]);
      if (err) clientErrors[`${s.key}.${f.columnName}`] = err;
    }));

    if (!orgNodeId) {
      clientErrors.organization = 'Select an organization node.';
    } else if (!editingId || orgChanged) {
      // the auto-selected root alone is not a real assignment while it has nodes below it
      const sel = lookups.nodes.find((n) => n.id === orgNodeId);
      if (sel && sel.isRoot && !sel.isHome && lookups.nodes.some((n) => n.parentId === sel.id)) {
        clientErrors.organization = 'Select a node below the organization.';
      }
    }

    if (!editingId && canManageAccounts) {
      accounts.forEach((a, i) => {
        if (!touched(a)) return;
        const role = lookups.roles.find((r) => String(r.id) === String(a.roleId));
        if (!role) clientErrors[`accounts.${i}.roleId`] = 'Choose a role.';
        if (!isEmployeeRole(role) && !a.username.trim()) clientErrors[`accounts.${i}.username`] = 'Enter a username.';
        if (a.password.length < MIN_PASSWORD) clientErrors[`accounts.${i}.password`] = `At least ${MIN_PASSWORD} characters.`;
      });
    }

    setErrors(clientErrors);
    if (Object.keys(clientErrors).length) {
      document.querySelector('.is-invalid, .text-danger')?.scrollIntoView({ block: 'center', behavior: 'smooth' });
      return;
    }

    setSaving(true);
    try {
      await uploadPending();

      const payloadSections = {};
      sections.forEach((s) => {
        const o = {};
        s.fields.forEach((f) => {
          if (skipCode(s, f)) return;
          let v = values[s.key]?.[f.columnName];
          if (f.dataType === 'FILE') {
            if (v?.file) {
              const u = uploadedRef.current[`${s.key}.${f.columnName}`];
              v = u ? { fileId: u.fileId, fileName: u.fileName } : null;
            } else v = v?.fileId ? { fileId: v.fileId, fileName: v.fileName } : null;
          } else if (f.dataType === 'BOOLEAN') v = !!v;
          else if (v === '' || v === undefined) v = null;
          o[f.columnName] = v;
        });
        payloadSections[s.key] = o;
      });

      const body = {
        organization: { nodeId: orgNodeId, effectiveFrom: orgChanged ? effectiveFrom : null },
        sections: payloadSections
      };

      if (editingId) {
        await updateEmployee(editingId, { ...body, version });
      } else {
        // every row is sent so server errors land on the same row; empty rows are ignored there
        const accountRows = canManageAccounts
          ? accounts.map((a) => (touched(a)
            ? { roleId: a.roleId ? Number(a.roleId) : null, username: a.username.trim() || null, password: a.password || null }
            : { roleId: null, username: null, password: null }))
          : [];
        await createEmployee({ ...body, accounts: accountRows });
      }

      uploadedRef.current = {}; // now referenced by the saved record
      await Swal.fire({ icon: 'success', title: editingId ? 'Employee updated' : 'Employee created', timer: 1100, showConfirmButton: false });

      if (!editingId && addAnother) {
        setValues(emptyValues(sections));
        setAccounts([makeAccount(lookups.roles, true)]);
        setOrgNodeId(null);
        setErrors({});
        setFormKey((k) => k + 1);
        window.scrollTo({ top: 0, behavior: 'smooth' });
      } else {
        navigate(backTo);
      }
    } catch (err) {
      setUploading(null);
      if (err?.response?.status === 409 && editingId) {
        const r = await Swal.fire({
          icon: 'warning', title: 'Changed by someone else', text: errMsg(err),
          showCancelButton: true, confirmButtonText: 'Reload latest', cancelButtonText: 'Keep editing'
        });
        if (r.isConfirmed) {
          uploadedRef.current = {};
          window.location.reload();
        }
      } else {
        const serverErrors = err?.response?.data?.errors;
        if (serverErrors) setErrors(serverErrors);
        Swal.fire('Unable to save', errMsg(err), 'error');
        setTimeout(() => document.querySelector('.is-invalid, .text-danger')?.scrollIntoView({ block: 'center', behavior: 'smooth' }), 100);
      }
    } finally {
      setSaving(false);
    }
  }

  async function toggleHistory() {
    if (history) { setHistory(null); return; }
    try { setHistory(await getEmployeeHistory(editingId)); } catch (e) { Swal.fire('Unable to load history', errMsg(e), 'error'); }
  }

  if (loading || !master || !lookups) return <div className="p-4"><span className="spinner-border spinner-border-sm" /></div>;

  if (!basicReady) {
    return (
      <div className="container py-4">
        <div className="alert alert-warning">
          The Basic Details section hasn't been finalized yet. An administrator must finish Employee Master Setup before employees can be added.
        </div>
      </div>
    );
  }

  if (notFound) {
    return (
      <div className="container py-4">
        <div className="alert alert-danger">This employee was not found, or you do not have access to it.</div>
        <button className="btn btn-outline-secondary btn-sm" onClick={() => navigate(backTo)}>Back to list</button>
      </div>
    );
  }

  const code = values.basic?.employee_code;
  const accErr = errors.accounts;

  return (
    <div className="container-fluid py-3" style={{ maxWidth: 1200 }}>
      <div className="d-flex align-items-center mb-3 gap-2">
        <button className="btn btn-outline-secondary btn-sm" onClick={() => navigate(backTo)}><i className="bi bi-arrow-left me-1" />Employees</button>
        <h5 className="mb-0">{editingId ? `Edit employee${code ? ` · ${code}` : ''}` : 'New employee'}</h5>
      </div>

      <form className="card mb-3" onSubmit={(e) => submit(e, false)} key={formKey} noValidate>
        <div className="card-body">
          {uploading && (
            <div className="alert alert-info py-2 d-flex align-items-center gap-2">
              <span className="spinner-border spinner-border-sm" />
              Uploading document {uploading.current} of {uploading.total}: <b className="text-truncate">{uploading.name}</b>
            </div>
          )}

          <div className="mb-3">
            <div className="text-uppercase text-muted small fw-bold border-bottom mb-3 pb-1">Organization</div>
            <OrgNodePicker lookups={lookups} value={orgNodeId} disabled={saving} error={errors.organization}
              onChange={(nid) => { setOrgNodeId(nid); setErrors((p) => ({ ...p, organization: undefined })); }} />

            {editingId && orgChanged && orgNodeId && (
              <div className="row mt-3">
                <div className="col-md-3">
                  <label className="form-label small fw-semibold mb-1">Transfer effective from <span className="text-danger">*</span></label>
                  <input type="date" className={`form-control ${errors['organization.effectiveFrom'] ? 'is-invalid' : ''}`}
                    value={effectiveFrom} min={origEffective || undefined} onChange={(e) => setEffectiveFrom(e.target.value)} />
                  {errors['organization.effectiveFrom'] && <div className="text-danger small mt-1">{errors['organization.effectiveFrom']}</div>}
                  <div className="form-text">The earlier assignment is closed the day before.</div>
                </div>
              </div>
            )}
            {!editingId && (
              <div className="row mt-3">
                <div className="col-md-3">
                  <label className="form-label small fw-semibold mb-1">Assigned from</label>
                  <input type="date" className={`form-control ${errors['organization.effectiveFrom'] ? 'is-invalid' : ''}`}
                    value={effectiveFrom} onChange={(e) => setEffectiveFrom(e.target.value)} />
                  {errors['organization.effectiveFrom'] && <div className="text-danger small mt-1">{errors['organization.effectiveFrom']}</div>}
                </div>
              </div>
            )}
          </div>

          {sections.map((s) => (
            <div key={s.id} className="mb-2">
              <div className="text-uppercase text-muted small fw-bold border-bottom mb-3 pb-1">
                {s.title} {!s.isMain && !s.isMandatory && <span className="text-lowercase fw-normal">(optional)</span>}
              </div>
              <div className="row">
                {s.fields.map((f) => (
                  <div key={f.id} className={f.dataType === 'FILE' ? 'col-md-6' : 'col-md-4'}>
                    <DynamicField
                      field={f}
                      value={values[s.key]?.[f.columnName]}
                      error={errors[`${s.key}.${f.columnName}`]}
                      lookups={lookups}
                      disabled={skipCode(s, f) || saving}
                      placeholder={skipCode(s, f) && !editingId ? 'Auto-generated' : undefined}
                      onChange={(v) => setField(s.key, f.columnName, v)}
                    />
                  </div>
                ))}
              </div>
            </div>
          ))}

          {!editingId && canManageAccounts && (
            <div className="mb-2">
              <div className="text-uppercase text-muted small fw-bold border-bottom mb-2 pb-1">Login account(s) <span className="text-lowercase fw-normal">(optional)</span></div>
              <p className="small text-muted">
                The Employee login uses the <b>employee code</b> as its username; other roles need a username. The pre-filled password is the
                default initial password, so the employee should change it after the first login. Remove a row to skip that login.
              </p>
              {accErr && <div className="text-danger small mb-2">{accErr}</div>}
              {accounts.length === 0 && <div className="small text-muted mb-2">No login will be created for this employee.</div>}

              {accounts.map((a, i) => {
                const role = lookups.roles.find((r) => String(r.id) === String(a.roleId));
                const usesCode = isEmployeeRole(role);
                const codeText = codeMode === 'AUTO' ? 'Employee code (generated on save)' : values.basic?.employee_code || 'Employee code';
                return (
                  <div className="row g-2 mb-2" key={i}>
                    <div className="col-md-3">
                      <select className={`form-select ${errors[`accounts.${i}.roleId`] ? 'is-invalid' : ''}`} value={a.roleId} disabled={saving}
                        onChange={(e) => setAccount(i, 'roleId', e.target.value)}>
                        <option value="">Role…</option>
                        {lookups.roles.map((r) => (
                          <option key={r.id} value={r.id} disabled={accounts.some((x, xi) => xi !== i && String(x.roleId) === String(r.id))}>{r.name}</option>
                        ))}
                      </select>
                      {errors[`accounts.${i}.roleId`] && <div className="text-danger small mt-1">{errors[`accounts.${i}.roleId`]}</div>}
                    </div>
                    <div className="col-md-4">
                      {usesCode ? (
                        <input className="form-control" value={codeText} disabled />
                      ) : (
                        <input className={`form-control ${errors[`accounts.${i}.username`] ? 'is-invalid' : ''}`} placeholder="Username" autoComplete="off"
                          value={a.username} disabled={saving} onChange={(e) => setAccount(i, 'username', e.target.value)} />
                      )}
                      {errors[`accounts.${i}.username`] && <div className="text-danger small mt-1">{errors[`accounts.${i}.username`]}</div>}
                    </div>
                    <div className="col-md-4">
                      <input className={`form-control ${errors[`accounts.${i}.password`] ? 'is-invalid' : ''}`} placeholder={`Password (min ${MIN_PASSWORD})`} autoComplete="off"
                        value={a.password} disabled={saving} onChange={(e) => setAccount(i, 'password', e.target.value)} />
                      {errors[`accounts.${i}.password`] && <div className="text-danger small mt-1">{errors[`accounts.${i}.password`]}</div>}
                    </div>
                    <div className="col-md-1">
                      <button type="button" className="btn btn-outline-danger" disabled={saving} onClick={() => setAccounts((p) => p.filter((_, idx) => idx !== i))}>
                        <i className="bi bi-x-lg" />
                      </button>
                    </div>
                  </div>
                );
              })}

              <button type="button" className="btn btn-link btn-sm p-0" disabled={saving} onClick={() => setAccounts((p) => [...p, makeAccount(lookups.roles)])}>
                + Add another account / role
              </button>
            </div>
          )}
        </div>

        <div className="card-footer d-flex gap-2 justify-content-end">
          <button type="button" className="btn btn-light" disabled={saving} onClick={() => navigate(backTo)}>Cancel</button>
          {!editingId && canAdd && (
            <button type="button" className="btn btn-outline-primary" disabled={saving} onClick={(e) => submit(e, true)}>Save &amp; add another</button>
          )}
          {(editingId ? canEdit : canAdd) && (
            <button className="btn btn-primary" disabled={saving}>{saving ? 'Saving…' : editingId ? 'Update employee' : 'Save employee'}</button>
          )}
        </div>
      </form>

      {editingId && canManageAccounts && (
        <EmployeeAccounts employeeId={editingId} employeeCode={values.basic?.employee_code} roles={lookups.roles} canAdd={canManageAccounts} />
      )}

      {editingId && (
        <div className="card mt-3">
          <div className="card-header d-flex align-items-center">
            <span className="fw-semibold">Change history</span>
            <button className="btn btn-sm btn-outline-secondary ms-auto" onClick={toggleHistory}>{history ? 'Hide' : 'Show'}</button>
          </div>
          {history && (
            <div className="table-responsive">
              <table className="table table-sm mb-0 small">
                <thead className="table-light"><tr><th>When</th><th>By</th><th>Change</th></tr></thead>
                <tbody>
                  {history.map((h, i) => (
                    <tr key={i}>
                      <td className="text-nowrap">{new Date(h.changedAt).toLocaleString()}</td>
                      <td>{h.changedByName || (h.changedBy ? `User #${h.changedBy}` : '—')}</td>
                      <td>
                        {h.action === 'CREATE'
                          ? 'Employee created'
                          : <><b>{h.label || h.columnName}</b>: <span className="text-muted">{h.oldValue ?? '—'}</span> → {h.newValue ?? '—'}</>}
                      </td>
                    </tr>
                  ))}
                  {history.length === 0 && <tr><td colSpan={3} className="text-muted text-center py-3">No history yet.</td></tr>}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </div>
  );
}