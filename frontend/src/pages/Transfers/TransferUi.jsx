import { useEffect, useState } from "react";
import api from "../../api/axios";
import { uploadFile, deleteFile } from "../../api/files";

// If your axios baseURL does NOT already end in /api, change this to "/api/transfers".
export const T = "/transfers";

export const nm = (x) =>
  x == null
    ? ""
    : typeof x !== "object"
    ? String(x)
    : x.name ?? x.depName ?? x.dep_name ?? x.designationName ?? x.designation_name ?? x.title ?? x.label ?? x.code ?? "";

export const who = (x) => {
  if (x == null) return "";
  if (typeof x !== "object") return String(x);
  const n = nm(x);
  const c = x.code ?? x.employeeCode;
  return c && n && c !== n ? `${n} (${c})` : n || c || "";
};

// Shows a path without its root node (first element), e.g. "Org › Zone › Depot" -> "Zone › Depot".
export const pathText = (p) => {
  let parts;
  if (Array.isArray(p)) parts = p.map(nm);
  else if (p && typeof p === "object") parts = [nm(p)];
  else parts = String(p || "").split(/\s*(?:›|»|>|\/)\s*/).filter(Boolean);
  if (parts.length > 1) parts = parts.slice(1);
  return parts.join(" › ");
};

export function Modal({ title, onClose, children, footer, size = "" }) {
  return (
    <>
      <div className="modal-backdrop fade show" />
      <div
        className="modal fade show d-block"
        tabIndex={-1}
        onMouseDown={(e) => {
          if (e.target === e.currentTarget) onClose();
        }}
      >
        <div className={`modal-dialog modal-dialog-scrollable ${size}`}>
          <div className="modal-content">
            <div className="modal-header">
              <h5 className="modal-title">{title}</h5>
              <button type="button" className="btn-close" onClick={onClose} />
            </div>
            <div className="modal-body">{children}</div>
            {footer && <div className="modal-footer">{footer}</div>}
          </div>
        </div>
      </div>
    </>
  );
}

export function Pager({ page, pageSize, total, onPage }) {
  const pages = Math.max(1, Math.ceil((total || 0) / pageSize));
  if (pages <= 1) return null;
  return (
    <div className="d-flex justify-content-between align-items-center mt-2">
      <small className="text-muted">
        Page {page} of {pages} · {total} records
      </small>
      <div className="btn-group btn-group-sm">
        <button className="btn btn-outline-secondary" disabled={page <= 1} onClick={() => onPage(page - 1)}>
          <i className="bi bi-chevron-left" />
        </button>
        <button className="btn btn-outline-secondary" disabled={page >= pages} onClick={() => onPage(page + 1)}>
          <i className="bi bi-chevron-right" />
        </button>
      </div>
    </div>
  );
}

/* ---------- files: uploaded only on submit ---------- */

export async function uploadOne(file) {
  const r = await uploadFile(file);
  const d = r?.data ?? r;
  return {
    fileId: d?.fileId ?? d?.id ?? d?.file_id,
    fileName: d?.fileName ?? d?.name ?? d?.file_name ?? file.name,
  };
}

export async function cleanupDocs(ids) {
  for (const id of ids || []) {
    try {
      await deleteFile(id);
    } catch {
      /* ignore */
    }
  }
}

// docs: [{label, file?, fileId?, fileName?}]  ->  { docs:[{label,fileId,fileName}], fresh:[fileId] }
export async function uploadDocs(docs) {
  const out = [];
  const fresh = [];
  try {
    for (const d of docs || []) {
      if (d.file) {
        const u = await uploadOne(d.file);
        fresh.push(u.fileId);
        out.push({ label: d.label, fileId: u.fileId, fileName: u.fileName });
      } else if (d.label || d.fileId) {
        out.push({ label: d.label, fileId: d.fileId || null, fileName: d.fileName || null });
      }
    }
  } catch (e) {
    await cleanupDocs(fresh);
    throw e;
  }
  return { docs: out, fresh };
}

export function DocPicker({ docs, setDocs }) {
  const set = (i, patch) => setDocs(docs.map((d, k) => (k === i ? { ...d, ...patch } : d)));
  return (
    <div>
      {docs.map((d, i) => (
        <div className="row g-2 mb-2" key={i}>
          <div className="col-md-5">
            <input
              className="form-control form-control-sm"
              placeholder="Document label"
              value={d.label || ""}
              onChange={(e) => set(i, { label: e.target.value })}
            />
          </div>
          <div className="col-md-6">
            <input
              type="file"
              className="form-control form-control-sm"
              onChange={(e) => set(i, { file: e.target.files?.[0] || null })}
            />
          </div>
          <div className="col-md-1">
            <button
              type="button"
              className="btn btn-sm btn-outline-danger"
              onClick={() => setDocs(docs.filter((_, k) => k !== i))}
            >
              <i className="bi bi-trash" />
            </button>
          </div>
        </div>
      ))}
      <button
        type="button"
        className="btn btn-sm btn-outline-primary"
        onClick={() => setDocs([...docs, { label: "", file: null }])}
      >
        <i className="bi bi-plus-lg" /> Add document
      </button>
    </div>
  );
}

/* ---------- employee search (uses GET /transfers/people) ---------- */

export function PersonPick({ value, onChange, disabled, placeholder = "Search by name or code (min 2 chars)" }) {
  const [q, setQ] = useState("");
  const [rows, setRows] = useState([]);

  useEffect(() => {
    if (q.trim().length < 2) {
      setRows([]);
      return;
    }
    const h = setTimeout(async () => {
      try {
        const r = await api.get(`${T}/people`, { params: { search: q.trim() } });
        setRows(r.data || []);
      } catch {
        setRows([]);
      }
    }, 300);
    return () => clearTimeout(h);
  }, [q]);

  if (value) {
    return (
      <div className="input-group">
        <input className="form-control" readOnly value={who(value)} />
        {!disabled && (
          <button type="button" className="btn btn-outline-secondary" onClick={() => onChange(null)}>
            Change
          </button>
        )}
      </div>
    );
  }
  return (
    <div className="position-relative">
      <input
        className="form-control"
        placeholder={placeholder}
        value={q}
        disabled={disabled}
        onChange={(e) => setQ(e.target.value)}
      />
      {rows.length > 0 && (
        <div className="list-group position-absolute w-100 shadow" style={{ zIndex: 30, maxHeight: 260, overflow: "auto" }}>
          {rows.map((p) => (
            <button
              type="button"
              key={p.id}
              className="list-group-item list-group-item-action"
              onClick={() => {
                onChange(p);
                setQ("");
                setRows([]);
              }}
            >
              <div className="fw-semibold">
                {p.name} <small className="text-muted">({p.code})</small>
              </div>
              <small className="text-muted">
                {[p.designation, p.place].filter(Boolean).join(" · ")}
              </small>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}