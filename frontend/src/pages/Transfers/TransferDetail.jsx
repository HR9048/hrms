import { useCallback, useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import Swal from "sweetalert2";
import api from "../../api/axios";
import { errMsg, fmtDate, fmtDateTime, StatusBadge, CheckList } from "./TransferBits";
import { DocPicker, Modal, T, cleanupDocs, nm, pathText, uploadDocs, uploadOne, who } from "./TransferUi";

const ACTION_META = {
  decide: { label: "Decide", icon: "bi-check2-square", cls: "btn-primary" },
  consent: { label: "Respond to consent", icon: "bi-hand-thumbs-up", cls: "btn-info" },
  order: { label: "Issue order", icon: "bi-file-earmark-text", cls: "btn-warning" },
  relieve: { label: "Relieve", icon: "bi-box-arrow-right", cls: "btn-success" },
  join: { label: "Record joining", icon: "bi-box-arrow-in-right", cls: "btn-success" },
  resubmit: { label: "Resubmit", icon: "bi-arrow-repeat", cls: "btn-primary" },
  objection: { label: "Raise objection", icon: "bi-exclamation-triangle", cls: "btn-outline-danger" },
  withdraw: { label: "Withdraw", icon: "bi-x-circle", cls: "btn-outline-secondary" },
};
const ORDER = ["decide", "consent", "order", "relieve", "join", "resubmit", "objection", "withdraw"];

const Info = ({ label, children }) =>
  children ? (
    <div className="col-sm-6 col-lg-4 mb-2">
      <div className="text-muted small">{label}</div>
      <div>{children}</div>
    </div>
  ) : null;

export default function TransferDetail() {
  const { id } = useParams();
  const [d, setD] = useState(null);
  const [err, setErr] = useState("");
  const [act, setAct] = useState(null);

  const load = useCallback(async () => {
    try {
      const r = await api.get(`${T}/${id}`);
      setD(r.data);
      setErr("");
    } catch (e) {
      setErr(errMsg(e));
    }
  }, [id]);

  useEffect(() => {
    load();
  }, [load]);

  const openFile = async (fileId, download) => {
    const w = window.open("", "_blank");
    try {
      const r = await api.get(`${T}/${id}/files/${fileId}/link`, { params: { download: download ? 1 : 0 } });
      if (w) w.location.href = r.data.url;
      else window.location.href = r.data.url;
    } catch (e) {
      if (w) w.close();
      Swal.fire({ icon: "error", title: "Could not open file", text: errMsg(e) });
    }
  };

  if (err) return <div className="alert alert-danger m-3">{err}</div>;
  if (!d) return <div className="p-4 text-center"><div className="spinner-border" /></div>;

  const r = d.request || {};
  const actions = ORDER.filter((a) => (d.actions || []).includes(a));
  const FileLink = ({ fileId, name }) =>
    fileId ? (
      <span className="btn-group btn-group-sm">
        <button className="btn btn-outline-primary" onClick={() => openFile(fileId, false)}>
          <i className="bi bi-eye" /> {name || "View"}
        </button>
        <button className="btn btn-outline-secondary" onClick={() => openFile(fileId, true)}>
          <i className="bi bi-download" />
        </button>
      </span>
    ) : null;

  return (
    <div className="container-fluid py-3">
      <div className="d-flex justify-content-between align-items-start flex-wrap gap-2 mb-3">
        <div>
          <Link to="/transfers" className="text-decoration-none small">
            <i className="bi bi-arrow-left" /> Back to transfers
          </Link>
          <h4 className="mb-0 mt-1">
            {r.requestNo} <StatusBadge status={r.status} />
          </h4>
          <div className="text-muted">{r.typeName}</div>
        </div>
        <div className="d-flex gap-2 flex-wrap">
          {actions.map((a) => (
            <button key={a} className={`btn btn-sm ${ACTION_META[a].cls}`} onClick={() => setAct(a)}>
              <i className={`bi ${ACTION_META[a].icon} me-1`} />
              {ACTION_META[a].label}
            </button>
          ))}
        </div>
      </div>

      {r.postingError && (
        <div className="alert alert-danger">Posting update failed: {r.postingError}</div>
      )}

      <div className="card shadow-sm mb-3">
        <div className="card-header fw-semibold">Request</div>
        <div className="card-body">
          <div className="row">
            <Info label="Employee">{who(r.employee)}</Info>
            <Info label="Exchange partner">{who(r.partner)}</Info>
            <Info label="Raised by">{who(r.createdBy)}</Info>
            <Info label="From">{pathText(r.fromPath)}</Info>
            <Info label="To">{pathText(r.toPath)}</Info>
            <Info label="Department">
              {r.department && (nm(r.department.from) || nm(r.department.to)) ? `${nm(r.department.from)} → ${nm(r.department.to) || "no change"}` : ""}
            </Info>
            <Info label="Designation">
              {r.designation && (nm(r.designation.from) || nm(r.designation.to)) ? `${nm(r.designation.from)} → ${nm(r.designation.to) || "no change"}` : ""}
            </Info>
            <Info label="Requested date">{fmtDate(r.requestedDate)}</Info>
            <Info label="Raised on">{fmtDateTime(r.createdAt)}</Info>
            <Info label="Consent">{r.consentStatus}</Info>
            <Info label="Window">{r.windowName}</Info>
            <Info label="Rank">{r.rankNo != null ? `#${r.rankNo}` : ""}</Info>
            <Info label="Selection">{r.selStatus}</Info>
            <Info label="Selection note">{r.selectionNote}</Info>
          </div>
          {r.reason && (
            <div className="mt-2">
              <div className="text-muted small">Reason</div>
              <div style={{ whiteSpace: "pre-wrap" }}>{r.reason}</div>
            </div>
          )}
        </div>
      </div>

      {(r.order?.no || r.relieveDate || r.joinDate || r.applyAt) && (
        <div className="card shadow-sm mb-3">
          <div className="card-header fw-semibold">Order &amp; execution</div>
          <div className="card-body">
            <div className="row">
              <Info label="Order no">{r.order?.no}</Info>
              <Info label="Order date">{fmtDate(r.order?.date)}</Info>
              <Info label="Order copy">
                <FileLink fileId={r.order?.fileId} name={r.order?.fileName} />
              </Info>
              <Info label="Relieved on">{fmtDate(r.relieveDate)}</Info>
              <Info label="Joined on">{fmtDate(r.joinDate)}</Info>
              <Info label="Posting applied">{r.postingApplied ? `Yes${r.applyAt ? ` (${fmtDateTime(r.applyAt)})` : ""}` : r.applyAt ? `Scheduled ${fmtDateTime(r.applyAt)}` : ""}</Info>
            </div>
          </div>
        </div>
      )}

      <div className="row g-3">
        <div className="col-lg-7">
          <div className="card shadow-sm mb-3">
            <div className="card-header fw-semibold">Approval steps</div>
            <div className="table-responsive">
              <table className="table table-sm align-middle mb-0">
                <thead className="table-light">
                  <tr>
                    <th>#</th>
                    <th>Step</th>
                    <th>Authority</th>
                    <th>Decision</th>
                    <th>By / when</th>
                  </tr>
                </thead>
                <tbody>
                  {(d.steps || []).map((s) => (
                    <tr key={s.stepOrder}>
                      <td>{s.stepOrder}</td>
                      <td>
                        {s.name}
                        {s.action && <div className="small text-muted">{s.action}</div>}
                      </td>
                      <td className="small">
                        {[s.authority, Array.isArray(s.roleCodes) ? s.roleCodes.join(", ") : s.roleCodes, s.anchor].filter(Boolean).join(" · ")}
                      </td>
                      <td>
                        {s.decision ? (
                          <span className={`badge bg-${s.decision === "APPROVE" ? "success" : s.decision === "REJECT" ? "danger" : "warning text-dark"}`}>
                            {s.decision}
                          </span>
                        ) : (
                          <span className="badge bg-light text-muted border">Pending</span>
                        )}
                        {s.remarks && <div className="small text-muted">{s.remarks}</div>}
                      </td>
                      <td className="small">
                        {s.actedByName}
                        {s.actedAt && <div className="text-muted">{fmtDateTime(s.actedAt)}</div>}
                      </td>
                    </tr>
                  ))}
                  {(d.steps || []).length === 0 && (
                    <tr>
                      <td colSpan={5} className="text-center text-muted">
                        No steps.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>

          {(d.consent || []).length > 0 && (
            <div className="card shadow-sm mb-3">
              <div className="card-header fw-semibold">Consent</div>
              <ul className="list-group list-group-flush">
                {d.consent.map((c) => (
                  <li key={c.id} className="list-group-item d-flex justify-content-between">
                    <span>{who(c.person)}</span>
                    {c.given === true ? (
                      <span className="badge bg-success">Accepted</span>
                    ) : c.given === false ? (
                      <span className="badge bg-danger">Declined</span>
                    ) : (
                      <span className="badge bg-secondary">Awaiting</span>
                    )}
                  </li>
                ))}
              </ul>
            </div>
          )}

          <div className="card shadow-sm mb-3">
            <div className="card-header fw-semibold">Activity</div>
            <ul className="list-group list-group-flush small">
              {(d.log || []).map((l, i) => (
                <li key={l.id ?? i} className="list-group-item">
                  <b>{l.action ?? l.event}</b>
                  {(l.byName ?? l.by ?? l.actorName) && <> · {who(l.byName ?? l.by ?? l.actorName)}</>}
                  <span className="text-muted"> · {fmtDateTime(l.at ?? l.createdAt)}</span>
                  {(l.remarks ?? l.note) && <div className="text-muted">{l.remarks ?? l.note}</div>}
                </li>
              ))}
              {(d.log || []).length === 0 && <li className="list-group-item text-muted">No activity yet.</li>}
            </ul>
          </div>
        </div>

        <div className="col-lg-5">
          <div className="card shadow-sm mb-3">
            <div className="card-header fw-semibold">Rule checks</div>
            <div className="card-body">
              <CheckList checks={d.checks || []} />
            </div>
          </div>

          <div className="card shadow-sm mb-3">
            <div className="card-header fw-semibold">Documents</div>
            <ul className="list-group list-group-flush">
              {(d.docs || []).map((x, i) => (
                <li key={x.id ?? i} className="list-group-item d-flex justify-content-between align-items-center">
                  <span>{x.label || x.fileName}</span>
                  <FileLink fileId={x.fileId} name={x.fileName ? "Open" : "View"} />
                </li>
              ))}
              {(d.docs || []).length === 0 && <li className="list-group-item text-muted">No documents.</li>}
            </ul>
          </div>

          {(d.objections || []).length > 0 && (
            <div className="card shadow-sm mb-3 border-danger">
              <div className="card-header fw-semibold text-danger">Objections</div>
              <ul className="list-group list-group-flush small">
                {d.objections.map((o, i) => (
                  <li key={o.id ?? i} className="list-group-item">
                    {o.text}
                    <div className="text-muted">
                      {who(o.byName ?? o.by ?? o.createdByName)} · {fmtDateTime(o.at ?? o.createdAt)}
                    </div>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      </div>

      {act && (
        <ActionModal
          action={act}
          id={id}
          needsRelaxationReason={d.needsRelaxationReason}
          onClose={() => setAct(null)}
          onDone={() => {
            setAct(null);
            load();
          }}
        />
      )}
    </div>
  );
}

function ActionModal({ action, id, needsRelaxationReason, onClose, onDone }) {
  const [f, setF] = useState({
    decision: "APPROVE",
    remarks: "",
    relaxationReason: "",
    accept: true,
    reason: "",
    orderNo: "",
    orderDate: "",
    date: "",
    text: "",
  });
  const [docs, setDocs] = useState([]);
  const [file, setFile] = useState(null);
  const [busy, setBusy] = useState(false);
  const set = (p) => setF((x) => ({ ...x, ...p }));
  const meta = ACTION_META[action];

  const validate = () => {
    if (action === "decide") {
      if (f.decision !== "APPROVE" && !f.remarks.trim()) return "Remarks are required";
      if (f.decision === "APPROVE" && needsRelaxationReason && !f.relaxationReason.trim()) return "Relaxation reason is required";
    }
    if (action === "resubmit" && !f.reason.trim()) return "Reason is required";
    if (action === "order" && (!f.orderNo.trim() || !f.orderDate)) return "Order number and date are required";
    if ((action === "relieve" || action === "join") && !f.date) return "Date is required";
    if (action === "objection" && !f.text.trim()) return "Objection text is required";
    return "";
  };

  const go = async () => {
    const m = validate();
    if (m) return Swal.fire({ icon: "warning", title: m });
    if (action === "withdraw") {
      const c = await Swal.fire({ icon: "question", title: "Withdraw this request?", showCancelButton: true, confirmButtonText: "Withdraw" });
      if (!c.isConfirmed) return;
    }
    setBusy(true);
    let fresh = [];
    try {
      let payload = {};
      if (action === "decide") payload = { decision: f.decision, remarks: f.remarks.trim(), relaxationReason: f.relaxationReason.trim() || null };
      else if (action === "consent") payload = { accept: f.accept, remarks: f.remarks.trim() };
      else if (action === "resubmit") {
        const up = await uploadDocs(docs);
        fresh = up.fresh;
        payload = { reason: f.reason.trim(), documents: up.docs };
      } else if (action === "order") {
        let fileId = null;
        let fileName = null;
        if (file) {
          const u = await uploadOne(file);
          fileId = u.fileId;
          fileName = u.fileName;
          fresh = [fileId];
        }
        payload = { orderNo: f.orderNo.trim(), orderDate: f.orderDate, fileId, fileName };
      } else if (action === "relieve" || action === "join") payload = { date: f.date };
      else if (action === "objection") payload = { text: f.text.trim() };

      await api.post(`${T}/${id}/${action}`, payload);
      Swal.fire({ icon: "success", title: "Done", timer: 1200, showConfirmButton: false });
      onDone();
    } catch (e) {
      await cleanupDocs(fresh);
      Swal.fire({ icon: "error", title: "Action failed", text: errMsg(e) });
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      title={meta.label}
      onClose={onClose}
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button className={`btn ${meta.cls}`} disabled={busy} onClick={go}>
            {busy && <span className="spinner-border spinner-border-sm me-1" />}
            {action === "decide" ? "Submit decision" : meta.label}
          </button>
        </>
      }
    >
      {action === "decide" && (
        <>
          <div className="btn-group w-100 mb-3">
            {["APPROVE", "RETURN", "REJECT"].map((x) => (
              <button
                key={x}
                type="button"
                className={`btn ${f.decision === x ? (x === "APPROVE" ? "btn-success" : x === "REJECT" ? "btn-danger" : "btn-warning") : "btn-outline-secondary"}`}
                onClick={() => set({ decision: x })}
              >
                {x === "RETURN" ? "Return" : x === "APPROVE" ? "Approve" : "Reject"}
              </button>
            ))}
          </div>
          {f.decision === "APPROVE" && needsRelaxationReason && (
            <div className="mb-3">
              <label className="form-label">Relaxation reason</label>
              <textarea className="form-control" rows={2} value={f.relaxationReason} onChange={(e) => set({ relaxationReason: e.target.value })} />
              <div className="form-text">Some rules are being relaxed; record why.</div>
            </div>
          )}
          <label className="form-label">Remarks{f.decision !== "APPROVE" ? " (required)" : ""}</label>
          <textarea className="form-control" rows={3} value={f.remarks} onChange={(e) => set({ remarks: e.target.value })} />
        </>
      )}

      {action === "consent" && (
        <>
          <div className="btn-group w-100 mb-3">
            <button type="button" className={`btn ${f.accept ? "btn-success" : "btn-outline-secondary"}`} onClick={() => set({ accept: true })}>
              Accept
            </button>
            <button type="button" className={`btn ${!f.accept ? "btn-danger" : "btn-outline-secondary"}`} onClick={() => set({ accept: false })}>
              Decline
            </button>
          </div>
          <label className="form-label">Remarks</label>
          <textarea className="form-control" rows={3} value={f.remarks} onChange={(e) => set({ remarks: e.target.value })} />
        </>
      )}

      {action === "withdraw" && <p className="mb-0">The request will be withdrawn and will no longer be processed.</p>}

      {action === "resubmit" && (
        <>
          <label className="form-label">Reason / changes made</label>
          <textarea className="form-control mb-3" rows={3} value={f.reason} onChange={(e) => set({ reason: e.target.value })} />
          <label className="form-label">Documents</label>
          <DocPicker docs={docs} setDocs={setDocs} />
        </>
      )}

      {action === "order" && (
        <>
          <div className="mb-3">
            <label className="form-label">Order number</label>
            <input className="form-control" value={f.orderNo} onChange={(e) => set({ orderNo: e.target.value })} />
          </div>
          <div className="mb-3">
            <label className="form-label">Order date</label>
            <input type="date" className="form-control" value={f.orderDate} onChange={(e) => set({ orderDate: e.target.value })} />
          </div>
          <label className="form-label">Order copy (optional)</label>
          <input type="file" className="form-control" onChange={(e) => setFile(e.target.files?.[0] || null)} />
        </>
      )}

      {(action === "relieve" || action === "join") && (
        <>
          <label className="form-label">{action === "relieve" ? "Relieving date" : "Joining date"}</label>
          <input type="date" className="form-control" value={f.date} onChange={(e) => set({ date: e.target.value })} />
        </>
      )}

      {action === "objection" && (
        <>
          <label className="form-label">Objection</label>
          <textarea className="form-control" rows={4} value={f.text} onChange={(e) => set({ text: e.target.value })} />
        </>
      )}
    </Modal>
  );
}