import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import Swal from "sweetalert2";
import api from "../../api/axios";
import { errMsg, fmtDate, CheckList } from "./TransferBits";
import DestPicker from "./TransferDest";
import { DocPicker, PersonPick, T, cleanupDocs, nm, pathText, uploadDocs, who } from "./TransferUi";

const EMPTY = {
  typeKey: "",
  categoryKey: "",
  toNodeId: null,
  toDepartmentId: "",
  toDesignationId: "",
  windowId: "",
  requestedDate: "",
  reason: "",
};

export default function TransferNew() {
  const nav = useNavigate();
  const [form, setForm] = useState(null);
  const [loading, setLoading] = useState(true);
  const [loadErr, setLoadErr] = useState("");
  const [f, setF] = useState(EMPTY);
  const [employee, setEmployee] = useState(null);
  const [partner, setPartner] = useState(null);
  const [docs, setDocs] = useState([]);
  const [result, setResult] = useState(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        const r = await api.get(`${T}/form`);
        setForm(r.data);
        const me = r.data?.me;
        if (me) setEmployee({ id: me.employeeId, name: me.person?.name, code: me.person?.code });
      } catch (e) {
        setLoadErr(errMsg(e));
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const type = useMemo(() => form?.types?.find((t) => t.typeKey === f.typeKey), [form, f.typeKey]);
  const category = useMemo(() => form?.categories?.find((c) => c.categoryKey === f.categoryKey), [form, f.categoryKey]);
  const windows = useMemo(
    () => (form?.windows || []).filter((w) => !w.typeKey || String(w.typeKey).toLowerCase() === String(f.typeKey).toLowerCase()),
    [form, f.typeKey]
  );
  const mode = type?.procMode;

  // any change invalidates a previous check
  const upd = (patch) => {
    setF((p) => ({ ...p, ...patch }));
    setResult(null);
  };

  useEffect(() => {
    if (!category?.docLabel) return;
    setDocs((d) => (d.some((x) => x.file) ? d : [{ label: category.docLabel, file: null }]));
  }, [category?.categoryKey]); // eslint-disable-line

  const missing = () => {
    if (!employee) return "Select the employee";
    if (!f.typeKey) return "Select the transfer type";
    if (type?.needsCategory && !f.categoryKey) return "Select the reason category";
    if (mode === "MUTUAL" && !partner) return "Select the exchange partner";
    if (mode === "MUTUAL" && partner?.id === employee?.id) return "Partner must be a different employee";
    if ((mode === "WINDOW" || windows.length > 0) && !f.windowId && mode === "WINDOW") return "Select an application window";
    if (mode !== "MUTUAL" && !f.toNodeId) return "Select the destination";
    if (type?.deptChange && !f.toDepartmentId) return "Select the new department";
    if (type?.desigChange && !f.toDesignationId) return "Select the new designation";
    if (!f.reason.trim()) return "Enter the reason";
    return "";
  };

  const body = (documents) => ({
    employeeId: employee?.id,
    partnerId: mode === "MUTUAL" ? partner?.id ?? null : null,
    typeKey: f.typeKey,
    categoryKey: type?.needsCategory ? f.categoryKey : null,
    toNodeId: mode === "MUTUAL" ? null : f.toNodeId,
    toDepartmentId: type?.deptChange ? Number(f.toDepartmentId) : null,
    toDesignationId: type?.desigChange ? Number(f.toDesignationId) : null,
    windowId: f.windowId ? Number(f.windowId) : null,
    requestedDate: f.requestedDate || null,
    reason: f.reason.trim(),
    documents,
  });

  const docMeta = () =>
    docs.filter((d) => d.label || d.file).map((d) => ({ label: d.label, fileId: null, fileName: d.file?.name || d.fileName || null }));

  const check = async () => {
    const m = missing();
    if (m) return Swal.fire({ icon: "warning", title: m });
    setBusy(true);
    try {
      const r = await api.post(`${T}/check`, body(docMeta()));
      setResult(r.data);
    } catch (e) {
      setResult(null);
      Swal.fire({ icon: "error", title: "Check failed", text: errMsg(e) });
    } finally {
      setBusy(false);
    }
  };

  const submit = async () => {
    const m = missing();
    if (m) return Swal.fire({ icon: "warning", title: m });
    const ok = await Swal.fire({
      icon: "question",
      title: "Submit transfer request?",
      showCancelButton: true,
      confirmButtonText: "Submit",
    });
    if (!ok.isConfirmed) return;
    setBusy(true);
    let fresh = [];
    try {
      const up = await uploadDocs(docs);
      fresh = up.fresh;
      const r = await api.post(T, body(up.docs));
      Swal.fire({ icon: "success", title: "Request submitted", timer: 1400, showConfirmButton: false });
      nav(`/transfers/${r.data.id}`);
    } catch (e) {
      await cleanupDocs(fresh);
      Swal.fire({ icon: "error", title: "Could not submit", text: errMsg(e) });
    } finally {
      setBusy(false);
    }
  };

  if (loading) return <div className="p-4 text-center"><div className="spinner-border" /></div>;
  if (loadErr) return <div className="alert alert-danger m-3">{loadErr}</div>;
  if (!form?.policy)
    return (
      <div className="alert alert-warning m-3">
        No transfer policy is active yet. Ask an administrator to finalize a transfer policy first.
      </div>
    );

  const lockedSelf = !form.canRaiseForOthers;

  return (
    <div className="container-fluid py-3">
      <div className="d-flex justify-content-between align-items-center mb-3">
        <h4 className="mb-0">
          <i className="bi bi-arrow-left-right me-2" />
          New Transfer Request
        </h4>
        <Link to="/transfers" className="btn btn-outline-secondary btn-sm">
          <i className="bi bi-list-ul" /> My requests
        </Link>
      </div>

      <div className="row g-3">
        <div className="col-lg-7">
          <div className="card shadow-sm">
            <div className="card-body">
              <div className="row g-3">
                <div className="col-12">
                  <label className="form-label">Employee</label>
                  <PersonPick
                    value={employee}
                    disabled={lockedSelf}
                    onChange={(p) => {
                      setEmployee(p);
                      setResult(null);
                    }}
                  />
                  {lockedSelf && form.me && <div className="form-text">You can raise a request only for yourself.</div>}
                  {!form.me && lockedSelf && (
                    <div className="text-danger small mt-1">Your login is not linked to an employee record.</div>
                  )}
                </div>

                {form.me?.path && employee?.id === form.me.employeeId && (
                  <div className="col-12">
                    <label className="form-label">Present place (From)</label>
                    <input className="form-control" readOnly value={pathText(form.me.path)} />
                  </div>
                )}

                <div className="col-md-6">
                  <label className="form-label">Transfer type</label>
                  <select
                    className="form-select"
                    value={f.typeKey}
                    onChange={(e) => {
                      setPartner(null);
                      upd({ typeKey: e.target.value, categoryKey: "", windowId: "", toNodeId: null });
                    }}
                  >
                    <option value="">— Select —</option>
                    {form.types.map((t) => (
                      <option key={t.typeKey} value={t.typeKey}>
                        {t.name}
                      </option>
                    ))}
                  </select>
                </div>

                {type?.needsCategory && (
                  <div className="col-md-6">
                    <label className="form-label">Reason category</label>
                    <select className="form-select" value={f.categoryKey} onChange={(e) => upd({ categoryKey: e.target.value })}>
                      <option value="">— Select —</option>
                      {form.categories.map((c) => (
                        <option key={c.categoryKey} value={c.categoryKey}>
                          {c.name}
                        </option>
                      ))}
                    </select>
                  </div>
                )}

                {f.typeKey && (mode === "WINDOW" || windows.length > 0) && (
                  <div className="col-12">
                    <label className="form-label">Application window{mode === "WINDOW" ? "" : " (optional)"}</label>
                    <select className="form-select" value={f.windowId} onChange={(e) => upd({ windowId: e.target.value })}>
                      <option value="">— Select —</option>
                      {windows.map((w) => (
                        <option key={w.id} value={w.id} disabled={!w.openNow}>
                          {w.name} ({fmtDate(w.applyFrom)} – {fmtDate(w.applyTo)}){w.openNow ? "" : " — closed"}
                        </option>
                      ))}
                    </select>
                    {windows.length === 0 && (
                      <div className="form-text text-danger">
                        No window is configured for this transfer type ({(form.windows || []).length} window(s) exist for other types).
                      </div>
                    )}
                  </div>
                )}

                {mode === "MUTUAL" && (
                  <div className="col-12">
                    <label className="form-label">Exchange partner</label>
                    <PersonPick
                      value={partner}
                      onChange={(p) => {
                        setPartner(p);
                        setResult(null);
                      }}
                    />
                    <div className="form-text">The partner will be asked for consent. Each takes the other's current place.</div>
                  </div>
                )}

                {mode !== "MUTUAL" && (
                  <div className="col-12">
                    <label className="form-label">Destination (To)</label>
                    <DestPicker key={f.typeKey} destinations={form.destinations} value={f.toNodeId} onChange={(v) => upd({ toNodeId: v })} />
                  </div>
                )}

                {type?.deptChange && (
                  <div className="col-md-6">
                    <label className="form-label">New department</label>
                    <select className="form-select" value={f.toDepartmentId} onChange={(e) => upd({ toDepartmentId: e.target.value })}>
                      <option value="">— Select —</option>
                      {form.departments.map((d) => (
                        <option key={d.id} value={d.id}>
                          {nm(d)}
                        </option>
                      ))}
                    </select>
                  </div>
                )}
                {type?.desigChange && (
                  <div className="col-md-6">
                    <label className="form-label">New designation</label>
                    <select className="form-select" value={f.toDesignationId} onChange={(e) => upd({ toDesignationId: e.target.value })}>
                      <option value="">— Select —</option>
                      {form.designations.map((d) => (
                        <option key={d.id} value={d.id}>
                          {nm(d)}
                        </option>
                      ))}
                    </select>
                  </div>
                )}

                <div className="col-md-6">
                  <label className="form-label">Requested date</label>
                  <input type="date" className="form-control" value={f.requestedDate} onChange={(e) => upd({ requestedDate: e.target.value })} />
                </div>

                <div className="col-12">
                  <label className="form-label">Reason</label>
                  <textarea className="form-control" rows={3} value={f.reason} onChange={(e) => upd({ reason: e.target.value })} />
                </div>

                <div className="col-12">
                  <label className="form-label">
                    Supporting documents{category?.docLabel ? <small className="text-muted"> — {category.docLabel}</small> : null}
                  </label>
                  <DocPicker
                    docs={docs}
                    setDocs={(d) => {
                      setDocs(d);
                      setResult(null);
                    }}
                  />
                  <div className="form-text">Files are uploaded only when you submit.</div>
                </div>
              </div>
            </div>
            <div className="card-footer d-flex gap-2 justify-content-end">
              <button className="btn btn-outline-primary" disabled={busy} onClick={check}>
                {busy ? <span className="spinner-border spinner-border-sm" /> : <i className="bi bi-shield-check" />} Check eligibility
              </button>
              <button className="btn btn-primary" disabled={busy || !result || result.blocked} onClick={submit}>
                <i className="bi bi-send" /> Submit
              </button>
            </div>
          </div>
        </div>

        <div className="col-lg-5">
          {!result ? (
            <div className="card border-dashed">
              <div className="card-body text-muted">
                <i className="bi bi-info-circle me-1" />
                Fill the form and press <b>Check eligibility</b> to see the rule results and the approval route before submitting.
              </div>
            </div>
          ) : (
            <div className="card shadow-sm">
              <div className="card-header fw-semibold">
                {result.typeName}
                {result.categoryName ? ` · ${result.categoryName}` : ""}
              </div>
              <div className="card-body">
                {result.blocked && (
                  <div className="alert alert-danger py-2">This request is blocked by policy and cannot be submitted.</div>
                )}
                {!result.blocked && result.needsRelaxation && (
                  <div className="alert alert-warning py-2">
                    Some rules need relaxation. The approver will have to record a reason.
                  </div>
                )}
                {result.consentRequired && (
                  <div className="alert alert-info py-2">Consent of the other party will be requested after submission.</div>
                )}

                <dl className="row mb-2 small">
                  <dt className="col-4">From</dt>
                  <dd className="col-8">{pathText(result.fromPath)}</dd>
                  <dt className="col-4">To</dt>
                  <dd className="col-8">{pathText(result.toPath)}</dd>
                  {result.partner && (
                    <>
                      <dt className="col-4">Partner</dt>
                      <dd className="col-8">{who(result.partner)}</dd>
                    </>
                  )}
                  {result.window && (
                    <>
                      <dt className="col-4">Window</dt>
                      <dd className="col-8">{nm(result.window)}</dd>
                    </>
                  )}
                  {result.seniorityEffect && (
                    <>
                      <dt className="col-4">Seniority</dt>
                      <dd className="col-8">{nm(result.seniorityEffect)}</dd>
                    </>
                  )}
                </dl>

                <h6>Rule checks</h6>
                <CheckList checks={result.checks || []} />

                {Array.isArray(result.steps) && result.steps.length > 0 && (
                  <>
                    <h6 className="mt-3">Approval route</h6>
                    <ol className="small ps-3 mb-0">
                      {result.steps.map((s, i) => (
                        <li key={i}>{typeof s === "object" ? s.name ?? s.stepName ?? s.authority : String(s)}</li>
                      ))}
                    </ol>
                  </>
                )}
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}