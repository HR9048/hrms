import { useRef } from 'react';
import Swal from 'sweetalert2';
import { useFilePreview } from './FilePreviewModal';
import { downloadFile } from '../api/files';

export const ymd = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const yearsAgo = (n) => { const d = new Date(); d.setFullYear(d.getFullYear() - n); return d; };
export const fileExt = (name) => (name?.split('.').pop() || '').toLowerCase();
const fmtSize = (b) => (b < 1048576 ? `${(b / 1024).toFixed(0)} KB` : `${(b / 1048576).toFixed(1)} MB`);

// Mirrors the server rules - the server remains the authority.
export function validateField(f, v) {
  const r = f.rules || {};

  if (f.dataType === 'FILE') return v ? null : f.isRequired ? 'Required' : null;

  const empty = v === undefined || v === null || String(v).trim() === '';
  if (empty) return f.isRequired && f.dataType !== 'BOOLEAN' ? 'Required' : null;
  const s = String(v).trim();

  if (['TEXT', 'EMAIL', 'PHONE'].includes(f.dataType)) {
    if (r.minLength && s.length < r.minLength) return `Minimum ${r.minLength} characters`;
    if (f.validationRegex) {
      try {
        if (!new RegExp(`^(?:${f.validationRegex})$`).test(s)) return f.formatHint ? `Expected format: ${f.formatHint}` : 'Invalid format';
      } catch { /* server validates anyway */ }
    }
    if (f.dataType === 'EMAIL' && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(s)) return 'Enter a valid email address';
  }

  if (f.dataType === 'NUMBER' || f.dataType === 'DECIMAL') {
    const n = Number(s);
    if (Number.isNaN(n)) return 'Enter a valid number';
    if (r.min != null && n < r.min) return `Must be at least ${r.min}`;
    if (r.max != null && n > r.max) return `Must be at most ${r.max}`;
  }

  if (f.dataType === 'DATE') {
    const today = ymd(new Date());
    if (r.noFuture && s > today) return 'Date cannot be in the future';
    if (r.noPast && s < today) return 'Date cannot be in the past';
    if (r.minAgeYears && s > ymd(yearsAgo(r.minAgeYears))) return `Age must be at least ${r.minAgeYears} years`;
    if (r.maxAgeYears && s < ymd(yearsAgo(r.maxAgeYears))) return `Age cannot be more than ${r.maxAgeYears} years`;
    if (r.minDate && s < r.minDate) return `Date must be on or after ${r.minDate}`;
    if (r.maxDate && s > r.maxDate) return `Date must be on or before ${r.maxDate}`;
  }
  return null;
}

// flat node list -> depth-first list with depth, for indented selects
export function nodeOptions(nodes = []) {
  const byParent = {};
  nodes.forEach((n) => { (byParent[n.parentId ?? 0] ||= []).push(n); });
  const out = [];
  const walk = (pid, depth) => (byParent[pid] || []).forEach((n) => { out.push({ ...n, depth }); walk(n.id, depth + 1); });
  walk(0, 0);
  return out;
}

export const indent = (o) => `${'\u00A0\u00A0'.repeat(o.depth)}${o.depth ? '└ ' : ''}${o.name}`;

// Nothing is uploaded here: a picked file stays in the browser ({ file }) until the form is submitted.
// A saved file comes from the server as { fileId, fileName }.
function FileInput({ field: f, value, onChange, disabled }) {
  const r = f.rules || {};
  const inputRef = useRef(null);
  const { previewFile, previewModal } = useFilePreview();
  const accept = r.accept?.length ? r.accept.map((e) => `.${e}`).join(',') : undefined;
  const maxMB = r.maxSizeMB || 20;

  function pick(e) {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) return;
    if (r.accept?.length && !r.accept.includes(fileExt(file.name))) {
      Swal.fire('Invalid file type', `Only ${r.accept.join(', ')} files are allowed.`, 'warning');
      return;
    }
    if (file.size > maxMB * 1024 * 1024) {
      Swal.fire('File too large', `"${file.name}" is larger than ${maxMB} MB.`, 'warning');
      return;
    }
    onChange({ file });
  }

  function openPending() {
    const url = URL.createObjectURL(value.file);
    window.open(url, '_blank');
    setTimeout(() => URL.revokeObjectURL(url), 60000);
  }

  const saved = value?.fileId ? { fileId: value.fileId, type: 'secure', originalFileName: value.fileName, storedFileName: value.fileName } : null;

  return (
    <div>
      <input ref={inputRef} type="file" className="d-none" accept={accept} onChange={pick} />

      {!value && (
        <button type="button" className="btn btn-outline-secondary btn-sm" disabled={disabled} onClick={() => inputRef.current?.click()}>
          <i className="bi bi-paperclip me-1" />Choose file
        </button>
      )}

      {value?.file && (
        <div className="d-flex align-items-center gap-2 border rounded px-2 py-1">
          <i className="bi bi-file-earmark text-secondary" />
          <span className="flex-grow-1 text-truncate small" title={value.file.name}>
            {value.file.name} <span className="text-muted">({fmtSize(value.file.size)}) · uploads when you save</span>
          </span>
          <button type="button" className="btn btn-sm btn-outline-primary" onClick={openPending} title="Preview"><i className="bi bi-eye" /></button>
          {!disabled && <button type="button" className="btn btn-sm btn-outline-danger" onClick={() => onChange(null)} title="Remove"><i className="bi bi-x-lg" /></button>}
        </div>
      )}

      {saved && (
        <div className="d-flex align-items-center gap-2 border rounded px-2 py-1">
          <i className="bi bi-file-earmark-lock text-secondary" />
          <span className="flex-grow-1 text-truncate small" title={saved.originalFileName}>{saved.originalFileName}</span>
          <button type="button" className="btn btn-sm btn-outline-primary" onClick={() => previewFile(saved)} title="View"><i className="bi bi-eye" /></button>
          <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => downloadFile(saved).catch(() => Swal.fire('Download failed', 'Please try again.', 'error'))} title="Download"><i className="bi bi-download" /></button>
          {!disabled && (
            <>
              <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => inputRef.current?.click()} title="Replace"><i className="bi bi-arrow-repeat" /></button>
              <button type="button" className="btn btn-sm btn-outline-danger" onClick={() => onChange(null)} title="Remove"><i className="bi bi-x-lg" /></button>
            </>
          )}
        </div>
      )}

      {(r.accept?.length || r.maxSizeMB) && (
        <div className="form-text">{r.accept?.length ? `Allowed: ${r.accept.join(', ')}. ` : ''}Max {maxMB} MB.</div>
      )}
      {previewModal}
    </div>
  );
}

export default function DynamicField({ field: f, value, onChange, error, lookups, disabled, placeholder }) {
  const id = `df-${f.sectionId}-${f.columnName}`;
  const set = (v) => onChange(f.forceUpper && typeof v === 'string' ? v.toUpperCase() : v);
  const r = f.rules || {};

  let input;
  if (f.dataType === 'LOOKUP') {
    const list = f.lookupSource === 'department' ? lookups?.departments : lookups?.designations;
    // inactive entries stay selectable only if the employee already has them
    const options = (list || [])
      .filter((o) => o.active !== 0 || String(o.id) === String(value))
      .map((o) => ({ id: o.id, label: o.active === 0 ? `${o.name} (inactive)` : o.name }));
    input = (
      <select id={id} className={`form-select ${error ? 'is-invalid' : ''}`} value={value ?? ''} disabled={disabled} onChange={(e) => set(e.target.value)}>
        <option value="">Select…</option>
        {options.map((o) => <option key={o.id} value={o.id}>{o.label}</option>)}
      </select>
    );
  } else if (f.dataType === 'DROPDOWN') {
    input = (
      <select id={id} className={`form-select ${error ? 'is-invalid' : ''}`} value={value ?? ''} disabled={disabled} onChange={(e) => set(e.target.value)}>
        <option value="">Select…</option>
        {f.options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
      </select>
    );
  } else if (f.dataType === 'BOOLEAN') {
    input = (
      <div className="form-check form-switch pt-1">
        <input id={id} type="checkbox" className="form-check-input" checked={value === true || value === 1} disabled={disabled} onChange={(e) => set(e.target.checked)} />
      </div>
    );
  } else if (f.dataType === 'FILE') {
    input = <FileInput field={f} value={value} onChange={onChange} disabled={disabled} />;
  } else {
    const type = { DATE: 'date', NUMBER: 'number', DECIMAL: 'number', EMAIL: 'email', PHONE: 'tel' }[f.dataType] || 'text';
    // the date picker itself stops invalid dates where a rule allows it
    const dateMin = f.dataType === 'DATE'
      ? [r.minDate, r.maxAgeYears ? ymd(yearsAgo(r.maxAgeYears)) : null, r.noPast ? ymd(new Date()) : null].filter(Boolean).sort().pop()
      : undefined;
    const dateMax = f.dataType === 'DATE'
      ? [r.maxDate, r.minAgeYears ? ymd(yearsAgo(r.minAgeYears)) : null, r.noFuture ? ymd(new Date()) : null].filter(Boolean).sort()[0]
      : undefined;

    input = (
      <input
        id={id}
        type={type}
        step={f.dataType === 'DECIMAL' ? '0.01' : f.dataType === 'NUMBER' ? '1' : undefined}
        min={f.dataType === 'DATE' ? dateMin : r.min ?? undefined}
        max={f.dataType === 'DATE' ? dateMax : r.max ?? undefined}
        className={`form-control ${error ? 'is-invalid' : ''}`}
        value={value ?? ''}
        maxLength={f.dataType === 'TEXT' ? f.maxLength || undefined : undefined}
        disabled={disabled}
        placeholder={placeholder || f.formatHint || ''}
        onChange={(e) => set(e.target.value)}
      />
    );
  }

  return (
    <div className="mb-3">
      <label htmlFor={id} className="form-label small fw-semibold mb-1">
        {f.label} {f.isRequired && <span className="text-danger">*</span>}
      </label>
      {input}
      {error && <div className="text-danger small mt-1">{error}</div>}
      {!error && f.helpText && <div className="form-text">{f.helpText}</div>}
      {!error && f.formatHint && <div className="form-text">Format: {f.formatHint}</div>}
    </div>
  );
}