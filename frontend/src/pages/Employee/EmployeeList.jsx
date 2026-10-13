import { useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import Swal from 'sweetalert2';
import { getMaster, getLookups, listEmployees, exportEmployees } from '../../api/employeeMaster';
import OrgNodeSelect from '../../components/OrgNodeSelect';
import usePageAccess from '../../hooks/usePageAccess';

// ---- adjust to your route paths ----
const NEW_PATH = '/employee/add';
const editPath = (id) => `/employee/${id}/edit`;
// ------------------------------------
console.log(editPath(123)); // Example usage
const FIXED = ['employee_code', 'first_name', 'last_name', 'status'];
const SIZES = [10, 25, 50, 100];
const errMsg = (e) => e?.response?.data?.message || e?.message || 'Something went wrong.';

export default function EmployeeList() {
    const navigate = useNavigate();
    const location = useLocation();
    const [sp, setSp] = useSearchParams();
    const spRef = useRef(sp);
    spRef.current = sp;

    const [master, setMaster] = useState(null);
    const [lookups, setLookups] = useState(null);
    const [data, setData] = useState({ total: 0, items: [] });
    const [loading, setLoading] = useState(false);
    const {canView, canAdd, canEdit, canDelete} = usePageAccess();
    // filters live in the URL so Back from the edit page restores the exact view
    const page = Math.max(1, Number(sp.get('page')) || 1);
    const pageSize = SIZES.includes(Number(sp.get('size'))) ? Number(sp.get('size')) : 25;
    const q = sp.get('q') || '';
    const node = sp.get('node') || '';
    const dept = sp.get('dept') || '';
    const desig = sp.get('desig') || '';
    const status = sp.get('status') || '';
    const sort = sp.get('sort') || 'employee_code';
    const dir = sp.get('dir') === 'desc' ? 'desc' : 'asc';
    const [qInput, setQInput] = useState(q);

    function update(patch) {
        const next = new URLSearchParams(spRef.current);
        Object.entries(patch).forEach(([k, v]) => (v === '' || v == null ? next.delete(k) : next.set(k, v)));
        if (!('page' in patch)) next.delete('page');
        setSp(next, { replace: true });
    }

    useEffect(() => {
        Promise.all([getMaster(), getLookups()])
            .then(([m, l]) => { setMaster(m); setLookups(l); })
            .catch((e) => Swal.fire('Unable to load', errMsg(e), 'error'));
    }, []);

    useEffect(() => {
        const t = setTimeout(() => { if (qInput !== q) update({ q: qInput }); }, 400);
        return () => clearTimeout(t);
    }, [qInput]); // eslint-disable-line react-hooks/exhaustive-deps

    const filterParams = useMemo(() => {
        const p = { sort, dir };
        if (q) p.search = q;
        if (node) p.nodeId = node;
        if (dept) p.departmentId = dept;
        if (desig) p.designationId = desig;
        if (status) p.status = status;
        return p;
    }, [q, node, dept, desig, status, sort, dir]);

    const finalized = !!master?.sections.find((s) => s.key === 'basic')?.isFinalized;

    useEffect(() => {
        if (!finalized) return undefined;
        let cancelled = false;
        setLoading(true);
        listEmployees({ ...filterParams, page, pageSize })
            .then((d) => !cancelled && setData(d))
            .catch((e) => !cancelled && Swal.fire('Unable to load employees', errMsg(e), 'error'))
            .finally(() => !cancelled && setLoading(false));
        return () => { cancelled = true; };
    }, [finalized, filterParams, page, pageSize]);

    const totalPages = Math.max(1, Math.ceil(data.total / pageSize));
    useEffect(() => { if (!loading && page > totalPages) update({ page: totalPages }); }, [totalPages]); // eslint-disable-line react-hooks/exhaustive-deps

    const fields = useMemo(() => (master?.sections || []).filter((s) => s.isFinalized).flatMap((s) => s.fields), [master]);
    const listCols = useMemo(() => fields.filter((f) => f.showInList && !FIXED.includes(f.columnName)), [fields]);
    const sortable = (f) => !['LOOKUP', 'FILE'].includes(f.dataType);

    const nodeById = useMemo(() => new Map((lookups?.nodes || []).map((n) => [n.id, n])), [lookups]);
    const pathOf = (id) => {
        const out = [];
        let cur = nodeById.get(id);
        let g = 0;
        while (cur && g++ < 30) { out.unshift(cur.name); cur = cur.parentId ? nodeById.get(cur.parentId) : null; }
        return out.join(' › ');
    };

    function toggleSort(key) {
        update({ sort: key, dir: sort === key && dir === 'asc' ? 'desc' : 'asc', page: '' });
    }

    async function doExport() {
        try {
            const blob = await exportEmployees(filterParams);
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `employees-${new Date().toISOString().slice(0, 10)}.csv`;
            document.body.appendChild(a);
            a.click();
            a.remove();
            URL.revokeObjectURL(url);
        } catch (e) {
            Swal.fire('Export failed', errMsg(e), 'error');
        }
    }

    const clearFilters = () => { setQInput(''); setSp(new URLSearchParams(), { replace: true }); };
    const hasFilters = q || node || dept || desig || status;

    const Th = ({ id, children }) => (
        <th role="button" className="text-nowrap user-select-none" onClick={() => toggleSort(id)}>
            {children} {sort === id && <i className={`bi bi-caret-${dir === 'asc' ? 'up' : 'down'}-fill small`} />}
        </th>
    );

    if (!master || !lookups) return <div className="p-4"><span className="spinner-border spinner-border-sm" /></div>;

    if (!finalized) {
        return (
            <div className="container py-4">
                <div className="alert alert-warning">The Basic Details section hasn't been finalized yet. Finish Employee Master Setup first.</div>
            </div>
        );
    }

    const from = data.total === 0 ? 0 : (page - 1) * pageSize + 1;
    const to = Math.min(page * pageSize, data.total);

    return (
        <div className="container-fluid py-3">
            <div className="d-flex align-items-center mb-3 gap-2">
                <h5 className="mb-0">Employees</h5>
                <span className="badge text-bg-light">{data.total.toLocaleString()}</span>
                <div className="ms-auto d-flex gap-2">
                    {canView && (
                        <button className="btn btn-outline-secondary btn-sm" onClick={doExport}><i className="bi bi-filetype-csv me-1" />Export</button>
                    )}
                    {canAdd && (
                        <button className="btn btn-primary btn-sm" onClick={() => navigate(NEW_PATH)}><i className="bi bi-plus-lg me-1" />Add employee</button>
                    )}
                </div>
            </div>

            <div className="card mb-3">
                <div className="card-body py-2 row g-2 align-items-center">
                    <div className="col-lg-3">
                        <input className="form-control form-control-sm" placeholder="Search code, name and searchable fields…" value={qInput} onChange={(e) => setQInput(e.target.value)} />
                    </div>
                    <div className="col-lg-3">
                        <OrgNodeSelect lookups={lookups} value={node} onChange={(v) => update({ node: v })} />
                    </div>
                    <div className="col-lg-2">
                        <select className="form-select form-select-sm" value={dept} onChange={(e) => update({ dept: e.target.value })}>
                            <option value="">All departments</option>
                            {lookups.departments.map((o) => <option key={o.id} value={o.id}>{o.name}{o.active === 0 ? ' (inactive)' : ''}</option>)}
                        </select>
                    </div>
                    <div className="col-lg-2">
                        <select className="form-select form-select-sm" value={desig} onChange={(e) => update({ desig: e.target.value })}>
                            <option value="">All designations</option>
                            {lookups.designations.map((o) => <option key={o.id} value={o.id}>{o.name}{o.active === 0 ? ' (inactive)' : ''}</option>)}
                        </select>
                    </div>
                    <div className="col-lg-1">
                        <select className="form-select form-select-sm" value={status} onChange={(e) => update({ status: e.target.value })}>
                            <option value="">Status</option>
                            <option value="ACTIVE">Active</option>
                            <option value="INACTIVE">Inactive</option>
                        </select>
                    </div>
                    <div className="col-lg-1 text-end">
                        {hasFilters && <button className="btn btn-link btn-sm p-0" onClick={clearFilters}>Clear</button>}
                    </div>
                </div>
            </div>

            <div className="card">
                <div className="table-responsive" style={{ opacity: loading ? 0.6 : 1, transition: 'opacity .15s' }}>
                    <table className="table table-sm table-hover align-middle mb-0">
                        <thead className="table-light">
                            <tr>
                                <Th id="employee_code">Code</Th>
                                <Th id="name">Name</Th>
                                <th className="text-nowrap">Organization</th>
                                {listCols.map((f) => (sortable(f) ? <Th key={f.id} id={`f${f.id}`}>{f.label}</Th> : <th key={f.id} className="text-nowrap">{f.label}</th>))}
                                <Th id="status">Status</Th>
                                <th style={{ width: 60 }} />
                            </tr>
                        </thead>
                        <tbody>
                            {data.items.map((e) => (
                                <tr key={e.employeeId} role={canEdit ? 'button' : undefined} onClick={canEdit ? () => navigate(editPath(e.employeeId), { state: { from: location.pathname + location.search } }) : undefined}>
                                    <td className="text-nowrap">{e.employeeCode}</td>
                                    <td>{e.firstName} {e.lastName}</td>
                                    <td className="small text-truncate" style={{ maxWidth: 280 }} title={e.orgNodeId ? pathOf(e.orgNodeId) : ''}>
                                        {e.orgNodeId ? pathOf(e.orgNodeId) : <span className="text-muted">—</span>}
                                    </td>
                                    {listCols.map((f) => <td key={f.id}>{e.values[f.id] ?? <span className="text-muted">—</span>}</td>)}
                                    <td><span className={`badge ${e.status === 'ACTIVE' ? 'text-bg-success' : 'text-bg-secondary'}`}>{e.status === 'ACTIVE' ? 'Active' : 'Inactive'}</span></td>
                                    <td className="text-end">{canEdit && <i className="bi bi-pencil text-primary" />}</td>
                                </tr>
                            ))}
                            {!loading && data.items.length === 0 && (
                                <tr><td colSpan={listCols.length + 5} className="text-center text-muted py-4">No employees found.</td></tr>
                            )}
                        </tbody>
                    </table>
                </div>

                <div className="card-footer d-flex flex-wrap align-items-center gap-3 small">
                    <span>{from.toLocaleString()}–{to.toLocaleString()} of {data.total.toLocaleString()}</span>
                    <div className="d-flex align-items-center gap-2 ms-auto">
                        <span>Rows</span>
                        <select className="form-select form-select-sm" style={{ width: 80 }} value={pageSize} onChange={(e) => update({ size: e.target.value })}>
                            {SIZES.map((s) => <option key={s} value={s}>{s}</option>)}
                        </select>
                        <div className="btn-group btn-group-sm">
                            <button className="btn btn-outline-secondary" disabled={page <= 1} onClick={() => update({ page: 1 })}><i className="bi bi-chevron-double-left" /></button>
                            <button className="btn btn-outline-secondary" disabled={page <= 1} onClick={() => update({ page: page - 1 })}><i className="bi bi-chevron-left" /></button>
                            <span className="btn btn-outline-secondary disabled">Page {page} / {totalPages}</span>
                            <button className="btn btn-outline-secondary" disabled={page >= totalPages} onClick={() => update({ page: page + 1 })}><i className="bi bi-chevron-right" /></button>
                            <button className="btn btn-outline-secondary" disabled={page >= totalPages} onClick={() => update({ page: totalPages })}><i className="bi bi-chevron-double-right" /></button>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
}