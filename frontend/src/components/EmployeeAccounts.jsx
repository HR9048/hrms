import { useEffect, useState } from 'react';
import Swal from 'sweetalert2';
import api from '../api/axios';

const errMsg = (e) => e?.response?.data?.message || e?.message || 'Something went wrong.';
const pick = (o, a, b) => o?.[a] ?? o?.[b];

export default function EmployeeAccounts({ employeeId, roles = [], canAdd }) {
  const [rows, setRows] = useState(null);
  const [form, setForm] = useState({ roleId: '', username: '', password: '' });
  const [saving, setSaving] = useState(false);

  const load = () =>
    api.get(`/employees/${employeeId}/accounts`).then((r) => setRows(r.data)).catch(() => setRows([]));

  useEffect(() => { load(); }, [employeeId]); // eslint-disable-line react-hooks/exhaustive-deps

  async function add(e) {
    e.preventDefault();
    if (!form.roleId || !form.username.trim() || form.password.length < 8) {
      Swal.fire('Check the form', 'Choose a role, enter a username and a password of at least 8 characters.', 'warning');
      return;
    }
    setSaving(true);
    try {
      await api.post(`/employees/${employeeId}/accounts`, {
        roleId: Number(form.roleId), username: form.username.trim(), password: form.password, isDefault: false
      });
      setForm({ roleId: '', username: '', password: '' });
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
        <form className="card-footer row g-2 align-items-end" onSubmit={add}>
          <div className="col-md-3">
            <select className="form-select form-select-sm" value={form.roleId} onChange={(e) => setForm({ ...form, roleId: e.target.value })}>
              <option value="">Role…</option>
              {roles.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
            </select>
          </div>
          <div className="col-md-3">
            <input className="form-control form-control-sm" placeholder="Username" autoComplete="off" value={form.username} onChange={(e) => setForm({ ...form, username: e.target.value })} />
          </div>
          <div className="col-md-3">
            <input type="password" className="form-control form-control-sm" placeholder="Password (min 8)" autoComplete="new-password" value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} />
          </div>
          <div className="col-md-3">
            <button className="btn btn-sm btn-outline-primary" disabled={saving}>{saving ? 'Adding…' : '+ Add another account'}</button>
          </div>
        </form>
      )}
    </div>
  );
}