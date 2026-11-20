import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import Swal from "sweetalert2";
import api from "../../api/axios";
import { errMsg, fmtDate } from "./TransferBits";
import DestPicker, { destLabel } from "./TransferDest";
import { Modal, T, nm } from "./TransferUi";

export default function TransferRotation() {
  const [d, setD] = useState(null);
  const [err, setErr] = useState("");
  const [overdueOnly, setOverdueOnly] = useState(false);
  const [q, setQ] = useState("");
  const [sel, setSel] = useState(new Set());
  const [dest, setDest] = useState({}); // employeeId -> nodeId
  const [pick, setPick] = useState(null); // { ids: [...], value }
  const [typeKey, setTypeKey] = useState("");
  const [reason, setReason] = useState("");
  const [toDept, setToDept] = useState("");
  const [toDesig, setToDesig] = useState("");
  const [busy, setBusy] = useState(false);
  const [results, setResults] = useState(null);

  const load = async () => {
    try {
      const r = await api.get(`${T}/rotation`);
      setD(r.data);
      setSel(new Set());
      setDest({});
    } catch (e) {
      setErr(errMsg(e));
    }
  };
  useEffect(() => {
    load();
  }, []);

  const rows = useMemo(() => {
    const ql = q.trim().toLowerCase();
    return (d?.rows || []).filter(
      (r) =>
        (!overdueOnly || r.overdue) &&
        (!ql || `${r.name} ${r.code} ${r.place} ${r.designation}`.toLowerCase().includes(ql))
    );
  }, [d, overdueOnly, q]);

  const type = (d?.types || []).find((t) => t.typeKey === typeKey);
  const allOn = rows.length > 0 && rows.every((r) => sel.has(r.employeeId));
  const toggle = (id) =>
    setSel((s) => {
      const n = new Set(s);
      n.has(id) ? n.delete(id) : n.add(id);
      return n;
    });

  const raise = async () => {
    const ids = [...sel];
    if (!typeKey) return Swal.fire({ icon: "warning", title: "Select the transfer type" });
    if (ids.length === 0) return Swal.fire({ icon: "warning", title: "Select at least one employee" });
    if (ids.some((id) => !dest[id])) return Swal.fire({ icon: "warning", title: "Every selected employee needs a destination" });
    if (!reason.trim()) return Swal.fire({ icon: "warning", title: "Enter the reason" });
    if (type?.deptChange && !toDept) return Swal.fire({ icon: "warning", title: "Select the new department" });
    if (type?.desigChange && !toDesig) return Swal.fire({ icon: "warning", title: "Select the new designation" });
    const c = await Swal.fire({
      icon: "question",
      title: `Raise ${ids.length} transfer request(s)?`,
      showCancelButton: true,
      confirmButtonText: "Raise",
    });
    if (!c.isConfirmed) return;
    setBusy(true);
    try {
      const r = await api.post(`${T}/rotation/transfer`, {
        typeKey,
        reason: reason.trim(),
        items: ids.map((employeeId) => ({ employeeId, toNodeId: dest[employeeId] })),
        toDepartmentId: type?.deptChange ? Number(toDept) : null,
        toDesignationId: type?.desigChange ? Number(toDesig) : null,
      });
      setResults(r.data);
    } catch (e) {
      Swal.fire({ icon: "error", title: "Could not raise transfers", text: errMsg(e) });
    } finally {
      setBusy(false);
    }
  };

  if (err) return <div className="alert alert-danger m-3">{err}</div>;
  if (!d) return <div className="p-4 text-center"><div className="spinner-border" /></div>;
  if (!d.policy)
    return <div className="alert alert-warning m-3">No transfer policy is active, so rotation tenure is not defined.</div>;

  const nameOf = (id) => (d.rows || []).find((r) => r.employeeId === id)?.name || id;

  return (
    <div className="container-fluid py-3">
      <div className="d-flex justify-content-between align-items-center mb-3">
        <h4 className="mb-0">
          <i className="bi bi-arrow-repeat me-2" />
          Rotation list
        </h4>
        <button className="btn btn-outline-secondary btn-sm" onClick={load}>
          <i className="bi bi-arrow-clockwise" /> Refresh
        </button>
      </div>

      <div className="row g-2 mb-3 align-items-center">
        <div className="col-md-4">
          <input className="form-control form-control-sm" placeholder="Filter by name, code, place…" value={q} onChange={(e) => setQ(e.target.value)} />
        </div>
        <div className="col-auto form-check ms-2">
          <input id="ovd" type="checkbox" className="form-check-input" checked={overdueOnly} onChange={(e) => setOverdueOnly(e.target.checked)} />
          <label htmlFor="ovd" className="form-check-label">
            Overdue only
          </label>
        </div>
        <div className="col text-end text-muted small">
          {rows.length} employee(s) · {rows.filter((r) => r.overdue).length} overdue
        </div>
      </div>

      <div className="card shadow-sm mb-3">
        <div className="table-responsive" style={{ maxHeight: "55vh" }}>
          <table className="table table-sm table-hover align-middle mb-0">
            <thead className="table-light" style={{ position: "sticky", top: 0, zIndex: 1 }}>
              <tr>
                {d.canRaise && (
                  <th style={{ width: 36 }}>
                    <input
                      type="checkbox"
                      className="form-check-input"
                      checked={allOn}
                      onChange={() => setSel(allOn ? new Set() : new Set(rows.map((r) => r.employeeId)))}
                    />
                  </th>
                )}
                <th>Employee</th>
                <th>Designation</th>
                <th>Present place</th>
                <th>Since</th>
                <th>Tenure</th>
                <th>Rule</th>
                {d.canRaise && <th>Destination</th>}
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.employeeId} className={r.overdue ? "table-warning" : ""}>
                  {d.canRaise && (
                    <td>
                      <input type="checkbox" className="form-check-input" checked={sel.has(r.employeeId)} onChange={() => toggle(r.employeeId)} />
                    </td>
                  )}
                  <td>
                    {r.name} <small className="text-muted">({r.code})</small>
                  </td>
                  <td>{r.designation}</td>
                  <td className="small">{r.place}</td>
                  <td>{fmtDate(r.since)}</td>
                  <td>
                    {r.months != null ? `${Math.floor(r.months / 12)}y ${r.months % 12}m` : ""}
                    {r.maxYears != null && <small className="text-muted"> / {r.maxYears}y</small>}
                    {r.overdue && <span className="badge bg-danger ms-2">Overdue</span>}
                  </td>
                  <td className="small">{r.rule}</td>
                  {d.canRaise && (
                    <td style={{ minWidth: 200 }}>
                      {sel.has(r.employeeId) ? (
                        <button
                          className={`btn btn-sm ${dest[r.employeeId] ? "btn-outline-success" : "btn-outline-primary"} text-start w-100`}
                          onClick={() => setPick({ ids: [r.employeeId], value: dest[r.employeeId] ?? null })}
                        >
                          {dest[r.employeeId] ? destLabel(d.destinations, dest[r.employeeId]) : "Choose…"}
                        </button>
                      ) : (
                        <span className="text-muted small">—</span>
                      )}
                    </td>
                  )}
                </tr>
              ))}
              {rows.length === 0 && (
                <tr>
                  <td colSpan={8} className="text-center text-muted py-4">
                    No employees to show.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {d.canRaise && (
        <div className="card shadow-sm">
          <div className="card-header fw-semibold">Raise transfers for {sel.size} selected</div>
          <div className="card-body">
            <div className="row g-3">
              <div className="col-md-4">
                <label className="form-label">Transfer type</label>
                <select className="form-select" value={typeKey} onChange={(e) => setTypeKey(e.target.value)}>
                  <option value="">— Select —</option>
                  {(d.types || []).map((t) => (
                    <option key={t.typeKey} value={t.typeKey}>
                      {t.name}
                    </option>
                  ))}
                </select>
              </div>
              {type?.deptChange && (
                <div className="col-md-4">
                  <label className="form-label">New department</label>
                  <select className="form-select" value={toDept} onChange={(e) => setToDept(e.target.value)}>
                    <option value="">— Select —</option>
                    {(d.departments || []).map((x) => (
                      <option key={x.id} value={x.id}>
                        {nm(x)}
                      </option>
                    ))}
                  </select>
                </div>
              )}
              {type?.desigChange && (
                <div className="col-md-4">
                  <label className="form-label">New designation</label>
                  <select className="form-select" value={toDesig} onChange={(e) => setToDesig(e.target.value)}>
                    <option value="">— Select —</option>
                    {(d.designations || []).map((x) => (
                      <option key={x.id} value={x.id}>
                        {nm(x)}
                      </option>
                    ))}
                  </select>
                </div>
              )}
              <div className="col-12">
                <label className="form-label">Reason</label>
                <textarea className="form-control" rows={2} value={reason} onChange={(e) => setReason(e.target.value)} />
              </div>
            </div>
          </div>
          <div className="card-footer d-flex justify-content-between">
            <button
              className="btn btn-outline-primary"
              disabled={sel.size === 0}
              onClick={() => setPick({ ids: [...sel], value: null })}
            >
              <i className="bi bi-geo-alt" /> Set destination for selected
            </button>
            <button className="btn btn-primary" disabled={busy || sel.size === 0} onClick={raise}>
              {busy && <span className="spinner-border spinner-border-sm me-1" />}
              <i className="bi bi-send" /> Raise transfers
            </button>
          </div>
        </div>
      )}

      {pick && (
        <Modal
          title={pick.ids.length === 1 ? `Destination — ${nameOf(pick.ids[0])}` : `Destination for ${pick.ids.length} employees`}
          onClose={() => setPick(null)}
          footer={
            <>
              <button className="btn btn-secondary" onClick={() => setPick(null)}>
                Cancel
              </button>
              <button
                className="btn btn-primary"
                disabled={!pick.value}
                onClick={() => {
                  setDest((m) => {
                    const n = { ...m };
                    pick.ids.forEach((id) => (n[id] = pick.value));
                    return n;
                  });
                  setPick(null);
                }}
              >
                Apply
              </button>
            </>
          }
        >
          <DestPicker destinations={d.destinations} value={pick.value} onChange={(v) => setPick((p) => ({ ...p, value: v }))} />
        </Modal>
      )}

      {results && (
        <Modal
          title={`Raised ${results.created ?? 0}, failed ${results.failed ?? 0}`}
          onClose={() => {
            setResults(null);
            load();
          }}
        >
          <ul className="list-group">
            {(results.results || []).map((x) => (
              <li key={x.employeeId} className="list-group-item d-flex justify-content-between align-items-start">
                <span>{nameOf(x.employeeId)}</span>
                {x.ok ? (
                  <Link to={`/transfers/${x.id}`} className="badge bg-success text-decoration-none">
                    Open #{x.id}
                  </Link>
                ) : (
                  <span className="text-danger small text-end">{x.error}</span>
                )}
              </li>
            ))}
          </ul>
        </Modal>
      )}
    </div>
  );
}