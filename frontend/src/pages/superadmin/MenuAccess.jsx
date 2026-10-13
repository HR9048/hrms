import { useEffect, useState } from 'react';
import { ShieldCheck } from 'lucide-react';
import api from '../../api/axios';

export default function MenuAccess() {
  const [roles, setRoles] = useState([]);
  const [roleId, setRoleId] = useState('');
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [message, setMessage] = useState('');

  useEffect(() => {
    api.get('/admin/roles').then((res) => {
      setRoles(res.data);
      const nonSuper = res.data.find((r) => r.role_code !== 'SUPERADMIN');
      if (nonSuper) setRoleId(nonSuper.id);
    });
  }, []);

  useEffect(() => {
    if (!roleId) return;
    setLoading(true);
    api.get(`/menus/admin/access/${roleId}`).then((res) => setRows(res.data)).finally(() => setLoading(false));
  }, [roleId]);

  function toggle(menuId, field) {
    setMessage('');
    setRows((prev) => prev.map((r) => (r.menu_id === menuId ? { ...r, [field]: r[field] ? 0 : 1 } : r)));
  }

  function toggleAll(field) {
    setMessage('');
    const allChecked = rows.every((r) => !!r[field]);
    setRows((prev) => prev.map((r) => ({ ...r, [field]: allChecked ? 0 : 1 })));
  }

  async function handleSave() {
    setMessage('');
    const permissions = rows.map((r) => ({
      menuId: r.menu_id,
      canView: !!r.can_view,
      canAdd: !!r.can_add,
      canEdit: !!r.can_edit,
      canDelete: !!r.can_delete
    }));
    try {
      await api.put(`/menus/admin/access/${roleId}`, { permissions });
      setMessage('Access updated.');
    } catch (err) {
      setMessage(err.response?.data?.message || 'Something went wrong.');
    }
  }

  const currentRole = roles.find((r) => String(r.id) === String(roleId));

  return (
    <div className="page">
      <div className="page-toolbar">
        <div>
          <h1>Role Menu Access</h1>
          <p className="muted">Choose which menus each role can view, and what they're allowed to do on them.</p>
        </div>
      </div>

      <div className="card">
        <div className="field" style={{ maxWidth: 320 }}>
          <label>Role</label>
          <select value={roleId} onChange={(e) => setRoleId(e.target.value)}>
            {roles.filter((r) => r.role_code !== 'SUPERADMIN').map((r) => (
              <option key={r.id} value={r.id}>{r.role_name}</option>
            ))}
          </select>
        </div>

        {loading ? (
          <p className="muted">Loading...</p>
        ) : rows.length === 0 ? (
          <div className="empty-state">
            <div className="empty-icon"><ShieldCheck size={20} /></div>
            <p>No menus to configure yet — create some in Menu Builder first.</p>
          </div>
        ) : (
          <table className="simple-table">
            <thead>
              <tr>
                <th>Menu</th>
                <th style={{ textAlign: 'center' }}>
                  View
                  <button type="button" className="btn btn-small" style={{ marginLeft: 6 }} onClick={() => toggleAll('can_view')}>All</button>
                </th>
                <th style={{ textAlign: 'center' }}>
                  Add
                  <button type="button" className="btn btn-small" style={{ marginLeft: 6 }} onClick={() => toggleAll('can_add')}>All</button>
                </th>
                <th style={{ textAlign: 'center' }}>
                  Edit
                  <button type="button" className="btn btn-small" style={{ marginLeft: 6 }} onClick={() => toggleAll('can_edit')}>All</button>
                </th>
                <th style={{ textAlign: 'center' }}>
                  Delete
                  <button type="button" className="btn btn-small" style={{ marginLeft: 6 }} onClick={() => toggleAll('can_delete')}>All</button>
                </th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.menu_id}>
                  <td>{r.menu_key}</td>
                  <td style={{ textAlign: 'center' }}>
                    <input type="checkbox" checked={!!r.can_view} onChange={() => toggle(r.menu_id, 'can_view')} />
                  </td>
                  <td style={{ textAlign: 'center' }}>
                    <input type="checkbox" checked={!!r.can_add} onChange={() => toggle(r.menu_id, 'can_add')} />
                  </td>
                  <td style={{ textAlign: 'center' }}>
                    <input type="checkbox" checked={!!r.can_edit} onChange={() => toggle(r.menu_id, 'can_edit')} />
                  </td>
                  <td style={{ textAlign: 'center' }}>
                    <input type="checkbox" checked={!!r.can_delete} onChange={() => toggle(r.menu_id, 'can_delete')} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        {message && <p className="muted">{message}</p>}
        <div className="form-actions">
          <button className="btn btn-primary" onClick={handleSave} disabled={rows.length === 0}>
            Save{currentRole ? ` for ${currentRole.role_name}` : ''}
          </button>
        </div>
      </div>
    </div>
  );
}
