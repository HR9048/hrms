import { useEffect, useState } from 'react';
import Swal from 'sweetalert2';
import { getEmployeeAccounts, addEmployeeAccount } from '../api/employeeMaster';
import { DEFAULT_PASSWORD, MIN_PASSWORD, isEmployeeRole } from '../utils/accountDefaults';

const errMsg = (e) => e?.response?.data?.message || e?.message || 'Something went wrong.';
const pick = (o, a, b) => o?.[a] ?? o?.[b];
const blank = () => ({ roleId: '', username: '', password: DEFAULT_PASSWORD });

export default function EmployeeAccounts({ employeeId, employeeCode, roles = [], canAdd }) {
  const [rows, setRows] = useState(null);
  const [form, setForm] = useState(blank);
  const [saving, setSaving] = useState(false);

  const load = () => getEmployeeAccounts(employeeId).then(setRows).catch(() => setRows([]));
  useEffect(() => { load(); }, [employeeId]); // eslint-disable-line react-hooks/exhaustive-deps

  const role = roles.find((r) => String(r.id) === String(form.roleId));
  const usesCode = isEmployeeRole(role);
  const hasRole = (r) => (rows || []).some((a) => String(pick(a, 'role_code', 'roleCode') || '').toUpperCase() === String(r.code || '').toUpperCase());

  async function add(e) {
    e.preventDefault();
    if (!role) { Swal.fire('Check the form', 'Choose a role.', 'warning'); return; }
    if (!usesCode && !form.username.trim()) { Swal.fire('Check the form', 'Enter a username.', 'warning'); return; }
    if (form.password.length < MIN_PASSWORD) { Swal.fire('Check the form', `The password needs at least ${MIN_PASSWORD} characters.`, 'warning'); return; }

    setSaving(true);
    try {
      await addEmployeeAccount(employeeId, {
        roleId: Number(form.roleId),
        username: usesCode ? null : form.username.trim(),
        password: form.password
      });
      setForm(blank());
      load();
    } catch (err) {
      Swal.fire('Unable to add account', errMsg(err), 'error');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="card mt-3">
      <div className="card-header fw-semibold">Login accounts</div>

      <div className="table-responsive">
        <table className="table table-sm align-middle mb-0">
          <thead className="table-light"><tr><th>Role</th><th>Username</th><th>Status</th><th /></tr></thead>
          <tbody>
            {(rows || []).map((a) => (
              <tr key={pick(a, 'id', 'Id')}>
                <td>{pick(a, 'role_name', 'roleName')}</td>
                <td>{a.username}</td>
                <td>
                  <span className={`badge ${Number(pick(a, 'is_active', 'isActive')) ? 'text-bg-success' : 'text-bg-secondary'}`}>
                    {Number(pick(a, 'is_active', 'isActive')) ? 'Active' : 'Inactive'}
                  </span>
                </td>
                <td>{Number(pick(a, 'is_default', 'isDefault')) ? <span className="badge text-bg-light">Default</span> : null}</td>
              </tr>
            ))}
            {rows && rows.length === 0 && <tr><td colSpan={4} className="text-center text-muted py-3">No login accounts.</td></tr>}
          </tbody>
        </table>
      </div>

      {canAdd && (
        <form className="card-footer row g-2 align-items-start" onSubmit={add}>
          <div className="col-md-3">
            <select className="form-select form-select-sm" value={form.roleId} onChange={(e) => setForm({ ...form, roleId: e.target.value })}>
              <option value="">Role…</option>
              {roles.map((r) => <option key={r.id} value={r.id} disabled={hasRole(r)}>{r.name}{hasRole(r) ? ' (already has)' : ''}</option>)}
            </select>
          </div>
          <div className="col-md-3">
            {usesCode
              ? <input className="form-control form-control-sm" disabled value={employeeCode || 'Employee code'} />
              : <input className="form-control form-control-sm" placeholder="Username" autoComplete="off" value={form.username} onChange={(e) => setForm({ ...form, username: e.target.value })} />}
          </div>
          <div className="col-md-3">
            <input className="form-control form-control-sm" placeholder={`Password (min ${MIN_PASSWORD})`} autoComplete="off" value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} />
          </div>
          <div className="col-md-3">
            <button className="btn btn-sm btn-outline-primary" disabled={saving}>{saving ? 'Adding…' : '+ Add account'}</button>
          </div>
        </form>
      )}
    </div>
  );
}