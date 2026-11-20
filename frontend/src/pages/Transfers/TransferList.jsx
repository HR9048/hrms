import { useCallback, useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import Swal from "sweetalert2";
import api from "../../api/axios";
import { errMsg, fmtDate, fmtDateTime, StatusBadge } from "./TransferBits";
import { Modal, Pager, T, cleanupDocs, uploadOne } from "./TransferUi";

// Edit to match the status keys your backend uses.
const STATUSES = ["SUBMITTED", "PENDING", "APPROVED", "REJECTED", "RETURNED", "WITHDRAWN", "ORDERED", "RELIEVED", "COMPLETED"];
const PAGE_SIZE = 20;

export default function TransferList() {
  const [sp, setSp] = useSearchParams();
  const view = ["inbox", "mine", "all"].includes(sp.get("view")) ? sp.get("view") : "inbox";
  const [status, setStatus] = useState("");
  const [typeKey, setTypeKey] = useState("");
  const [search, setSearch] = useState("");
  const [applied, setApplied] = useState({ search: "" });
  const [page, setPage] = useState(1);
  const [data, setData] = useState({ total: 0, items: [], inboxCount: 0 });
  const [types, setTypes] = useState([]);
  const [loading, setLoading] = useState(true);
  const [sel, setSel] = useState(new Set());
  const [bulk, setBulk] = useState(false);

  useEffect(() => {
    api
      .get(`${T}/form`)
      .then((r) => setTypes(r.data?.types || []))
      .catch(() => {});
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const r = await api.get(T, {
        params: { view, status: status || undefined, typeKey: typeKey || undefined, search: applied.search || undefined, page, pageSize: PAGE_SIZE },
      });
      setData(r.data);
      setSel(new Set());
    } catch (e) {
      Swal.fire({ icon: "error", title: "Could not load", text: errMsg(e) });
    } finally {
      setLoading(false);
    }
  }, [view, status, typeKey, applied, page]);

  useEffect(() => {
    load();
  }, [load]);

  const setView = (v) => {
    setSp({ view: v });
    setPage(1);
  };
  const toggle = (id) =>
    setSel((s) => {
      const n = new Set(s);
      n.has(id) ? n.delete(id) : n.add(id);
      return n;
    });
  const items = data.items || [];
  const allOn = items.length > 0 && items.every((i) => sel.has(i.id));
  const canBulk = view !== "mine";

  return (
    <div className="container-fluid py-3">
      <div className="d-flex justify-content-between align-items-center mb-3 flex-wrap gap-2">
        <h4 className="mb-0">
          <i className="bi bi-arrow-left-right me-2" />
          Transfers
        </h4>
        <div className="d-flex gap-2">
          {canBulk && sel.size > 0 && (
            <button className="btn btn-warning btn-sm" onClick={() => setBulk(true)}>
              <i className="bi bi-file-earmark-text" /> Bulk order ({sel.size})
            </button>
          )}
          <Link to="/transfers/new" className="btn btn-primary btn-sm">
            <i className="bi bi-plus-lg" /> New request
          </Link>
        </div>
      </div>

      <ul className="nav nav-tabs mb-3">
        {[
          ["inbox", "Inbox"],
          ["mine", "My requests"],
          ["all", "All"],
        ].map(([k, label]) => (
          <li className="nav-item" key={k}>
            <button className={`nav-link ${view === k ? "active" : ""}`} onClick={() => setView(k)}>
              {label}
              {k === "inbox" && data.inboxCount > 0 && <span className="badge bg-danger ms-2">{data.inboxCount}</span>}
            </button>
          </li>
        ))}
      </ul>

      <div className="row g-2 mb-3">
        <div className="col-md-3">
          <select className="form-select form-select-sm" value={typeKey} onChange={(e) => { setTypeKey(e.target.value); setPage(1); }}>
            <option value="">All types</option>
            {types.map((t) => (
              <option key={t.typeKey} value={t.typeKey}>
                {t.name}
              </option>
            ))}
          </select>
        </div>
        <div className="col-md-3">
          <select className="form-select form-select-sm" value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }}>
            <option value="">All statuses</option>
            {STATUSES.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
        </div>
        <div className="col-md-4">
          <div className="input-group input-group-sm">
            <input
              className="form-control"
              placeholder="Request no, employee name or code"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  setApplied({ search });
                  setPage(1);
                }
              }}
            />
            <button className="btn btn-outline-secondary" onClick={() => { setApplied({ search }); setPage(1); }}>
              <i className="bi bi-search" />
            </button>
          </div>
        </div>
      </div>

      <div className="card shadow-sm">
        <div className="table-responsive">
          <table className="table table-hover table-sm align-middle mb-0">
            <thead className="table-light">
              <tr>
                {canBulk && (
                  <th style={{ width: 36 }}>
                    <input
                      type="checkbox"
                      className="form-check-input"
                      checked={allOn}
                      onChange={() => setSel(allOn ? new Set() : new Set(items.map((i) => i.id)))}
                    />
                  </th>
                )}
                <th>Request</th>
                <th>Type</th>
                <th>Employee</th>
                <th>From → To</th>
                <th>Requested</th>
                <th>Rank</th>
                <th>Status</th>
                <th>Raised</th>
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr>
                  <td colSpan={9} className="text-center py-4">
                    <div className="spinner-border spinner-border-sm" />
                  </td>
                </tr>
              ) : items.length === 0 ? (
                <tr>
                  <td colSpan={9} className="text-center text-muted py-4">
                    Nothing here.
                  </td>
                </tr>
              ) : (
                items.map((r) => (
                  <tr key={r.id}>
                    {canBulk && (
                      <td>
                        <input type="checkbox" className="form-check-input" checked={sel.has(r.id)} onChange={() => toggle(r.id)} />
                      </td>
                    )}
                    <td>
                      <Link to={`/transfers/${r.id}`} className="fw-semibold text-decoration-none">
                        {r.requestNo}
                      </Link>
                    </td>
                    <td>{r.typeName}</td>
                    <td>
                      {r.employeeName} <small className="text-muted">({r.employeeCode})</small>
                      {r.partnerName && <div className="small text-muted">⇄ {r.partnerName}</div>}
                    </td>
                    <td className="small">
                      {r.fromName} <i className="bi bi-arrow-right mx-1" /> {r.toName || "—"}
                    </td>
                    <td>{fmtDate(r.requestedDate)}</td>
                    <td>
                      {r.rankNo != null ? `#${r.rankNo}` : ""}
                      {r.selStatus && <div className="small text-muted">{r.selStatus}</div>}
                    </td>
                    <td>
                      <StatusBadge status={r.status} />
                    </td>
                    <td className="small">{fmtDateTime(r.createdAt)}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>
      <Pager page={page} pageSize={PAGE_SIZE} total={data.total} onPage={setPage} />

      {bulk && (
        <BulkOrder
          ids={[...sel]}
          onClose={() => setBulk(false)}
          onDone={() => {
            setBulk(false);
            load();
          }}
        />
      )}
    </div>
  );
}

function BulkOrder({ ids, onClose, onDone }) {
  const [orderNo, setOrderNo] = useState("");
  const [orderDate, setOrderDate] = useState("");
  const [file, setFile] = useState(null);
  const [busy, setBusy] = useState(false);

  const go = async () => {
    if (!orderNo.trim() || !orderDate) return Swal.fire({ icon: "warning", title: "Order number and date are required" });
    setBusy(true);
    let fresh = null;
    try {
      let fileId = null;
      let fileName = null;
      if (file) {
        const u = await uploadOne(file);
        fileId = u.fileId;
        fileName = u.fileName;
        fresh = [fileId];
      }
      const r = await api.post(`${T}/bulk-order`, { ids, orderNo: orderNo.trim(), orderDate, fileId, fileName });
      await Swal.fire({
        icon: "success",
        title: "Order recorded",
        text: r.data?.message || `${ids.length} request(s) processed`,
      });
      onDone();
    } catch (e) {
      await cleanupDocs(fresh);
      Swal.fire({ icon: "error", title: "Bulk order failed", text: errMsg(e) });
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      title={`Bulk order — ${ids.length} request(s)`}
      onClose={onClose}
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button className="btn btn-primary" disabled={busy} onClick={go}>
            {busy && <span className="spinner-border spinner-border-sm me-1" />} Issue order
          </button>
        </>
      }
    >
      <div className="mb-3">
        <label className="form-label">Order number</label>
        <input className="form-control" value={orderNo} onChange={(e) => setOrderNo(e.target.value)} />
      </div>
      <div className="mb-3">
        <label className="form-label">Order date</label>
        <input type="date" className="form-control" value={orderDate} onChange={(e) => setOrderDate(e.target.value)} />
      </div>
      <div className="mb-1">
        <label className="form-label">Order copy (optional)</label>
        <input type="file" className="form-control" onChange={(e) => setFile(e.target.files?.[0] || null)} />
      </div>
      <div className="form-text">Only requests that are ready for an order will be accepted by the server.</div>
    </Modal>
  );
}