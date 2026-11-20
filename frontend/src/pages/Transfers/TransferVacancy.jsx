import { useEffect, useState } from 'react';
import Swal from 'sweetalert2';
import { getVacancy, saveVacancy, deleteVacancy } from '../../api/transfers';
import useOrgScope from '../../hooks/useOrgScope';
import OrgNodePicker from '../../components/OrgNodePicker';
import { errMsg } from './TransferBits';

export default function TransferVacancy() {
  const { data: scope } = useOrgScope();
  const [nodeId, setNodeId] = useState(null);
  const [data, setData] = useState(null);
  const [add, setAdd] = useState({ designationId: '', sanctioned: '' });

  const load = (id) => getVacancy(id).then(setData).catch((e) => { setData(null); Swal.fire('Unable to load', errMsg(e), 'error'); });
  useEffect(() => { if (nodeId) load(nodeId); else setData(null); }, [nodeId]);

  async function save(designationId, sanctioned) {
    try { await saveVacancy({ nodeId, designationId: Number(designationId), sanctioned: Number(sanctioned) }); setAdd({ designationId: '', sanctioned: '' }); load(nodeId); }
    catch (e) { Swal.fire('Unable to save', errMsg(e), 'error'); }
  }
  async function remove(designationId) { try { await deleteVacancy(nodeId, designationId); load(nodeId); } catch (e) { Swal.fire('Unable to remove', errMsg(e), 'error'); } }

  if (!scope) return <div className="p-4 text-center"><span className="spinner-border spinner-border-sm" /></div>;

  return (
    <div className="container-fluid py-3" style={{ maxWidth: 900 }}>
      <h5 className="mb-1">Sanctioned strength and vacancy</h5>
      <p className="small text-muted">Vacancy = sanctioned posts − employees working there − transfers already coming in. Choose a node to see and set its sanctioned strength per designation.</p>
      <div className="card mb-3"><div className="card-body"><OrgNodePicker filter lookups={scope} value={nodeId} onChange={(id) => setNodeId(id)} /></div></div>

      {nodeId && data && (
        <div className="card">
          <div className="table-responsive">
            <table className="table table-sm align-middle mb-0">
              <thead className="table-light"><tr><th>Designation</th><th style={{ width: 130 }}>Sanctioned</th><th>Working</th><th>Incoming</th><th>Vacancy</th><th /></tr></thead>
              <tbody>
                {data.rows.map((r) => (
                  <tr key={r.designationId}>
                    <td>{r.designation}</td>
                    <td><input type="number" min="0" className="form-control form-control-sm" defaultValue={r.sanctioned ?? ''} onBlur={(e) => e.target.value !== String(r.sanctioned ?? '') && e.target.value !== '' && save(r.designationId, e.target.value)} /></td>
                    <td>{r.working}</td><td>{r.incoming}</td>
                    <td>{r.vacancy == null ? <span className="text-muted">not set</span> : <span className={`badge ${r.vacancy > 0 ? 'text-bg-success' : 'text-bg-danger'}`}>{r.vacancy}</span>}</td>
                    <td className="text-end">{r.sanctioned != null && <button className="btn btn-sm btn-outline-danger" onClick={() => remove(r.designationId)}><i className="bi bi-x" /></button>}</td>
                  </tr>
                ))}
                {data.rows.length === 0 && <tr><td colSpan={6} className="text-center text-muted py-3">No designation recorded for this node yet.</td></tr>}
              </tbody>
            </table>
          </div>
          <div className="card-footer row g-2 align-items-end">
            <div className="col-md-5">
              <label className="form-label small mb-1">Add a designation</label>
              <select className="form-select form-select-sm" value={add.designationId} onChange={(e) => setAdd({ ...add, designationId: e.target.value })}>
                <option value="">Select…</option>{data.designations.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
              </select>
            </div>
            <div className="col-md-3"><label className="form-label small mb-1">Sanctioned</label><input type="number" min="0" className="form-control form-control-sm" value={add.sanctioned} onChange={(e) => setAdd({ ...add, sanctioned: e.target.value })} /></div>
            <div className="col-md-2"><button className="btn btn-primary btn-sm w-100" disabled={!add.designationId || add.sanctioned === ''} onClick={() => save(add.designationId, add.sanctioned)}>Add</button></div>
          </div>
        </div>
      )}
    </div>
  );
}