import { useEffect, useState } from 'react';
import Swal from 'sweetalert2';
import {
  getMaster, createSection, updateSection, deleteSection, finalizeSection,
  createField, updateField, deleteField, reorderFields, saveSettings
} from '../../api/employeeMaster';
import { FORMAT_PRESETS } from '../../utils/formatPresets';

const TYPES = [
  ['TEXT', 'Text'], ['NUMBER', 'Whole number'], ['DECIMAL', 'Decimal'], ['DATE', 'Date'], ['DROPDOWN', 'Dropdown'],
  ['BOOLEAN', 'Yes / No'], ['EMAIL', 'Email'], ['PHONE', 'Phone'], ['FILE', 'File / Document']
];
const REQUIRED_LOCKED = ['employee_code', 'first_name', 'status'];
const ALWAYS_IN_LIST = ['employee_code', 'first_name', 'last_name', 'status'];
const TEXTY = ['TEXT', 'EMAIL', 'PHONE'];
const errMsg = (e) => e?.response?.data?.message || e?.message || 'Something went wrong.';

const parseOptions = (text) =>
  text.split('\n').map((l) => l.trim()).filter(Boolean).map((l) => {
    const [a, ...rest] = l.split('=');
    return rest.length ? { value: a.trim(), label: rest.join('=').trim() } : { value: l, label: l };
  });

const num = (v) => (v === '' || v == null ? null : Number(v));
const rulesFromField = (r = {}) => ({
  minLength: r.minLength ?? '', min: r.min ?? '', max: r.max ?? '', noFuture: !!r.noFuture, noPast: !!r.noPast,
  minAgeYears: r.minAgeYears ?? '', maxAgeYears: r.maxAgeYears ?? '', minDate: r.minDate ?? '', maxDate: r.maxDate ?? '',
  accept: (r.accept || []).join(', '), maxSizeMB: r.maxSizeMB ?? ''
});
const rulesToBody = (r) => ({
  minLength: num(r.minLength), min: num(r.min), max: num(r.max), noFuture: !!r.noFuture, noPast: !!r.noPast,
  minAgeYears: num(r.minAgeYears), maxAgeYears: num(r.maxAgeYears), minDate: r.minDate || null, maxDate: r.maxDate || null,
  accept: r.accept ? r.accept.split(',').map((x) => x.trim()).filter(Boolean) : null, maxSizeMB: num(r.maxSizeMB)
});

// ---------------------------------------------------------------- section form
function SectionForm({ section, onSaved, onCancel }) {
  const isNew = !section;
  const [title, setTitle] = useState(section?.title ?? '');
  const [key, setKey] = useState('');
  const [mandatory, setMandatory] = useState(section?.isMandatory ?? false);
  const [saving, setSaving] = useState(false);

  async function submit(e) {
    e.preventDefault();
    setSaving(true);
    try {
      if (isNew) await createSection({ title, key, isMandatory: mandatory });
      else await updateSection(section.id, { title, key: null, isMandatory: mandatory });
      onSaved();
    } catch (err) {
      Swal.fire('Unable to save', errMsg(err), 'error');
    } finally {
      setSaving(false);
    }
  }

  return (
    <form className="card mb-3 border-primary" onSubmit={submit}>
      <div className="card-header fw-semibold">{isNew ? 'Add section (creates its own database table)' : `Edit section: ${section.title}`}</div>
      <div className="card-body row g-3">
        <div className="col-md-4">
          <label className="form-label small">Title *</label>
          <input className="form-control" value={title} placeholder="e.g. Aadhaar Details" onChange={(e) => setTitle(e.target.value)} required />
        </div>
        {isNew && (
          <div className="col-md-3">
            <label className="form-label small">Key (table name)</label>
            <input className="form-control" value={key} placeholder="auto from title" onChange={(e) => setKey(e.target.value)} />
            <div className="form-text">Table: emp_sec_{key || '…'}</div>
          </div>
        )}
        {!section?.isMain && (
          <div className="col-md-5 d-flex align-items-end">
            <div className="form-check">
              <input id="s-man" type="checkbox" className="form-check-input" checked={mandatory} onChange={(e) => setMandatory(e.target.checked)} />
              <label htmlFor="s-man" className="form-check-label small">
                Mandatory for every employee <span className="text-muted">(otherwise it is only checked when someone fills it in)</span>
              </label>
            </div>
          </div>
        )}
      </div>
      <div className="card-footer d-flex gap-2 justify-content-end">
        <button type="button" className="btn btn-light btn-sm" onClick={onCancel}>Cancel</button>
        <button className="btn btn-primary btn-sm" disabled={saving}>{saving ? 'Saving…' : isNew ? 'Create section' : 'Save'}</button>
      </div>
    </form>
  );
}

// ---------------------------------------------------------------- field form
function FieldForm({ sectionId, field, onSaved, onCancel }) {
  const isNew = !field;
  const isCore = !!field?.isCore;
  const alwaysInList = isCore && ALWAYS_IN_LIST.includes(field.columnName);

  const [f, setF] = useState({
    label: field?.label ?? '', dataType: field?.dataType ?? 'TEXT', key: '', maxLength: field?.maxLength ?? 100,
    isRequired: field?.isRequired ?? false, isUnique: field?.isUnique ?? false, forceUpper: field?.forceUpper ?? false,
    showInList: field?.showInList ?? false, isSearchable: field?.isSearchable ?? false,
    validationRegex: field?.validationRegex ?? '', formatHint: field?.formatHint ?? '', helpText: field?.helpText ?? '',
    optionsText: (field?.options ?? []).map((o) => (o.value === o.label ? o.label : `${o.value}=${o.label}`)).join('\n')
  });
  const [rules, setRules] = useState(rulesFromField(field?.rules));
  const [preset, setPreset] = useState(field?.validationRegex ? 'custom' : 'any');
  const [saving, setSaving] = useState(false);
  const set = (k, v) => setF((p) => ({ ...p, [k]: v }));
  const setRule = (k, v) => setRules((p) => ({ ...p, [k]: v }));

  const uniqueAllowed = isNew ? ['TEXT', 'NUMBER', 'EMAIL', 'PHONE'].includes(f.dataType) : isCore && ['email', 'phone'].includes(field.columnName);
  const requiredLocked = isCore && REQUIRED_LOCKED.includes(field.columnName);
  const isTexty = TEXTY.includes(f.dataType);
  const showOptions = f.dataType === 'DROPDOWN' && !isCore;
  const canSearch = isTexty && !alwaysInList;

  function pickPreset(key) {
    setPreset(key);
    const p = FORMAT_PRESETS.find((x) => x.key === key);
    if (key !== 'custom') setF((prev) => ({ ...prev, validationRegex: p.regex, formatHint: p.hint, forceUpper: p.forceUpper }));
  }

  async function submit(e) {
    e.preventDefault();
    setSaving(true);
    const body = {
      label: f.label, isRequired: f.isRequired, isUnique: f.isUnique, forceUpper: f.forceUpper,
      showInList: f.showInList, isSearchable: f.isSearchable && canSearch,
      validationRegex: isTexty ? f.validationRegex : null, formatHint: f.formatHint, helpText: f.helpText,
      options: showOptions ? parseOptions(f.optionsText) : null, rules: rulesToBody(rules)
    };
    try {
      if (isNew) await createField({ ...body, sectionId, dataType: f.dataType, key: f.key, maxLength: f.dataType === 'TEXT' ? Number(f.maxLength) : null });
      else await updateField(field.id, body);
      onSaved();
    } catch (err) {
      Swal.fire('Unable to save', errMsg(err), 'error');
    } finally {
      setSaving(false);
    }
  }

  const Check = ({ id, label, checked, disabled, onChange }) => (
    <div className="form-check">
      <input id={id} type="checkbox" className="form-check-input" checked={checked} disabled={disabled} onChange={(e) => onChange(e.target.checked)} />
      <label htmlFor={id} className="form-check-label small">{label}</label>
    </div>
  );

  return (
    <form className="card mb-3 border-primary" onSubmit={submit}>
      <div className="card-header fw-semibold">{isNew ? 'Add field' : `Edit: ${field.label}`}</div>

      <div className="card-body row g-3">
        <div className="col-md-4">
          <label className="form-label small">Label *</label>
          <input className="form-control" value={f.label} onChange={(e) => set('label', e.target.value)} required />
        </div>
        <div className="col-md-3">
          <label className="form-label small">Type</label>
          <select className="form-select" value={f.dataType} disabled={!isNew} onChange={(e) => set('dataType', e.target.value)}>
            {isCore ? <option value={f.dataType}>{f.dataType}</option> : TYPES.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
          </select>
        </div>
        {isNew && (
          <div className="col-md-3">
            <label className="form-label small">Column key</label>
            <input className="form-control" value={f.key} placeholder="auto" onChange={(e) => set('key', e.target.value)} />
          </div>
        )}
        {isNew && f.dataType === 'TEXT' && (
          <div className="col-md-2">
            <label className="form-label small">Max length</label>
            <input type="number" min="1" max="255" className="form-control" value={f.maxLength} onChange={(e) => set('maxLength', e.target.value)} />
          </div>
        )}

        <div className="col-12 d-flex flex-wrap gap-4">
          <Check id="f-req" label="Required" checked={f.isRequired} disabled={requiredLocked} onChange={(v) => set('isRequired', v)} />
          {uniqueAllowed && <Check id="f-uniq" label="Unique across employees" checked={f.isUnique} onChange={(v) => set('isUnique', v)} />}
          {isTexty && <Check id="f-up" label="Force UPPERCASE" checked={f.forceUpper} onChange={(v) => set('forceUpper', v)} />}
          <Check id="f-list" label={`Show in employee list${alwaysInList ? ' (always)' : ''}`} checked={alwaysInList || f.showInList} disabled={alwaysInList} onChange={(v) => set('showInList', v)} />
          {isTexty && <Check id="f-search" label={`Include in list search${alwaysInList ? ' (always)' : ''}`} checked={alwaysInList || f.isSearchable} disabled={alwaysInList} onChange={(v) => set('isSearchable', v)} />}
        </div>

        {/* ---- validation rules by type ---- */}
        {isTexty && (
          <>
            <div className="col-md-2">
              <label className="form-label small">Min length</label>
              <input type="number" min="0" className="form-control" value={rules.minLength} onChange={(e) => setRule('minLength', e.target.value)} />
            </div>
            <div className="col-md-3">
              <label className="form-label small">Format</label>
              <select className="form-select" value={preset} onChange={(e) => pickPreset(e.target.value)}>
                {FORMAT_PRESETS.map((p) => <option key={p.key} value={p.key}>{p.label}</option>)}
              </select>
            </div>
            <div className="col-md-3">
              <label className="form-label small">Pattern (regex)</label>
              <input className="form-control font-monospace" value={f.validationRegex} disabled={preset !== 'custom'} onChange={(e) => set('validationRegex', e.target.value)} />
            </div>
            <div className="col-md-4">
              <label className="form-label small">Format hint shown to users</label>
              <input className="form-control" value={f.formatHint} onChange={(e) => set('formatHint', e.target.value)} />
            </div>
          </>
        )}

        {(f.dataType === 'NUMBER' || f.dataType === 'DECIMAL') && (
          <>
            <div className="col-md-3">
              <label className="form-label small">Minimum value</label>
              <input type="number" className="form-control" value={rules.min} onChange={(e) => setRule('min', e.target.value)} />
            </div>
            <div className="col-md-3">
              <label className="form-label small">Maximum value</label>
              <input type="number" className="form-control" value={rules.max} onChange={(e) => setRule('max', e.target.value)} />
            </div>
          </>
        )}

        {f.dataType === 'DATE' && (
          <>
            <div className="col-12 d-flex flex-wrap gap-4">
              <Check id="r-nf" label="No future dates" checked={rules.noFuture} onChange={(v) => setRule('noFuture', v)} />
              <Check id="r-np" label="No past dates" checked={rules.noPast} onChange={(v) => setRule('noPast', v)} />
            </div>
            <div className="col-md-3">
              <label className="form-label small">Minimum age (years)</label>
              <input type="number" min="0" className="form-control" value={rules.minAgeYears} placeholder="e.g. 18 for date of birth" onChange={(e) => setRule('minAgeYears', e.target.value)} />
            </div>
            <div className="col-md-3">
              <label className="form-label small">Maximum age (years)</label>
              <input type="number" min="0" className="form-control" value={rules.maxAgeYears} onChange={(e) => setRule('maxAgeYears', e.target.value)} />
            </div>
            <div className="col-md-3">
              <label className="form-label small">Earliest date</label>
              <input type="date" className="form-control" value={rules.minDate} onChange={(e) => setRule('minDate', e.target.value)} />
            </div>
            <div className="col-md-3">
              <label className="form-label small">Latest date</label>
              <input type="date" className="form-control" value={rules.maxDate} onChange={(e) => setRule('maxDate', e.target.value)} />
            </div>
          </>
        )}

        {f.dataType === 'FILE' && (
          <>
            <div className="col-md-6">
              <label className="form-label small">Allowed file types</label>
              <input className="form-control" value={rules.accept} placeholder="pdf, jpg, png" onChange={(e) => setRule('accept', e.target.value)} />
            </div>
            <div className="col-md-3">
              <label className="form-label small">Max size (MB)</label>
              <input type="number" min="1" max="50" className="form-control" value={rules.maxSizeMB} placeholder="20" onChange={(e) => setRule('maxSizeMB', e.target.value)} />
            </div>
          </>
        )}

        <div className="col-12">
          <label className="form-label small">Help text (shown under the field)</label>
          <input className="form-control" maxLength={200} value={f.helpText} onChange={(e) => set('helpText', e.target.value)} />
        </div>

        {showOptions && (
          <div className="col-12">
            <label className="form-label small">Options (one per line, or value=Label)</label>
            <textarea className="form-control" rows={4} value={f.optionsText} onChange={(e) => set('optionsText', e.target.value)} />
          </div>
        )}
      </div>

      <div className="card-footer d-flex gap-2 justify-content-end">
        <button type="button" className="btn btn-light btn-sm" onClick={onCancel}>Cancel</button>
        <button className="btn btn-primary btn-sm" disabled={saving}>{saving ? 'Saving…' : isNew ? 'Create column' : 'Save'}</button>
      </div>
    </form>
  );
}

// ---------------------------------------------------------------- page
export default function EmployeeMasterSetup() {
  const [master, setMaster] = useState(null);
  const [sectionEdit, setSectionEdit] = useState(null); // null | 'new' | section
  const [fieldEdit, setFieldEdit] = useState(null);     // null | { sectionId, field|null }
  const [codeMode, setCodeMode] = useState('MANUAL');
  const [codePattern, setCodePattern] = useState('');

  async function load() {
    try {
      const m = await getMaster();
      setMaster(m);
      setCodeMode(m.config.codeMode);
      setCodePattern(m.config.codePattern || '');
    } catch (e) {
      Swal.fire('Unable to load', errMsg(e), 'error');
    }
  }
  useEffect(() => { load(); }, []);

  if (!master) return <div className="p-4"><span className="spinner-border spinner-border-sm" /></div>;

  const { sections, config } = master;
  const basic = sections.find((s) => s.key === 'basic');
  const codeLocked = !basic || basic.isFinalized;

  async function guarded(fn, failTitle) {
    try { await fn(); load(); } catch (e) { Swal.fire(failTitle, errMsg(e), 'error'); }
  }

  async function move(section, index, dir) {
    const ids = section.fields.map((f) => f.id);
    const j = index + dir;
    if (j < 0 || j >= ids.length) return;
    [ids[index], ids[j]] = [ids[j], ids[index]];
    guarded(() => reorderFields(section.id, ids), 'Unable to reorder');
  }

  async function removeField(f) {
    const ok = await Swal.fire({
      title: `Delete "${f.label}"?`, text: 'The database column is dropped too.', icon: 'warning',
      showCancelButton: true, confirmButtonText: 'Delete', confirmButtonColor: '#dc3545'
    });
    if (ok.isConfirmed) guarded(() => deleteField(f.id), 'Unable to delete');
  }

  async function removeSection(s) {
    const ok = await Swal.fire({
      title: `Delete section "${s.title}"?`, text: 'Its database table and all its fields are dropped.', icon: 'warning',
      showCancelButton: true, confirmButtonText: 'Delete', confirmButtonColor: '#dc3545'
    });
    if (ok.isConfirmed) guarded(() => deleteSection(s.id), 'Unable to delete');
  }

  async function finalize(s) {
    const r = await Swal.fire({
      title: `Finalize "${s.title}"?`,
      html: 'After this, <b>no field of this section can be added, edited, deleted or reordered</b>, and its list/search settings are fixed too. This cannot be undone.<br/>Type <b>FINALIZE</b> to confirm.',
      input: 'text', icon: 'warning', showCancelButton: true, confirmButtonText: 'Finalize', confirmButtonColor: '#dc3545',
      preConfirm: (v) => (v === 'FINALIZE' ? true : Swal.showValidationMessage('Type FINALIZE exactly'))
    });
    if (r.isConfirmed) guarded(() => finalizeSection(s.id), 'Unable to finalize');
  }

  async function saveCode() {
    try {
      await saveSettings({ codeMode, codePattern });
      Swal.fire({ icon: 'success', title: 'Saved', timer: 1000, showConfirmButton: false });
      load();
    } catch (e) {
      Swal.fire('Unable to save', errMsg(e), 'error');
    }
  }

  const preview = (() => {
    if (codeMode !== 'AUTO' || !codePattern) return '';
    const now = new Date();
    return codePattern.replace(/\{(YYYY|YY|MM|SEQ:(\d{1,2}))\}/g, (_, t, n) =>
      t === 'YYYY' ? now.getFullYear()
        : t === 'YY' ? String(now.getFullYear()).slice(-2)
        : t === 'MM' ? String(now.getMonth() + 1).padStart(2, '0')
        : '1'.padStart(Number(n), '0'));
  })();

  return (
    <div className="container-fluid py-3">
      <div className="d-flex align-items-center mb-3 gap-2">
        <h5 className="mb-0">Employee Master Setup</h5>
        { !codeLocked && (
          <button className="btn btn-primary btn-sm ms-auto" onClick={() => setSectionEdit('new')}>
            <i className="bi bi-plus-lg me-1" />Add section
          </button>
        )}
        { codeLocked && <span className="badge text-bg-danger ms-auto"><i className="bi bi-lock-fill me-1" />Employee Details is finalized</span> }
      </div>

      <div className="alert alert-light border small py-2">
        Basic, Contact and Job details live in the employee table. Every section you add gets <b>its own table</b> (one row per employee).
        Each section is finalized on its own and is locked for good afterwards, so you can keep adding new document sections later.
      </div>

      {sectionEdit && (
        <SectionForm
          key={sectionEdit === 'new' ? 'new' : sectionEdit.id}
          section={sectionEdit === 'new' ? null : sectionEdit}
          onSaved={() => { setSectionEdit(null); load(); }}
          onCancel={() => setSectionEdit(null)}
        />
      )}

      <div className="card mb-3">
        <div className="card-header fw-semibold">Employee code</div>
        <div className="card-body row g-3 align-items-end">
          <div className="col-md-3">
            <div className="form-check">
              <input id="cm1" type="radio" className="form-check-input" disabled={codeLocked} checked={codeMode === 'MANUAL'} onChange={() => setCodeMode('MANUAL')} />
              <label htmlFor="cm1" className="form-check-label">Entered manually (format on the field)</label>
            </div>
            <div className="form-check">
              <input id="cm2" type="radio" className="form-check-input" disabled={codeLocked} checked={codeMode === 'AUTO'} onChange={() => setCodeMode('AUTO')} />
              <label htmlFor="cm2" className="form-check-label">Auto-generated</label>
            </div>
          </div>

          {codeMode === 'AUTO' && (
            <div className="col-md-4">
              <label className="form-label small">Pattern</label>
              <input className="form-control font-monospace" value={codePattern} disabled={codeLocked} placeholder="EMP-{YYYY}-{SEQ:5}" onChange={(e) => setCodePattern(e.target.value)} />
              <div className="form-text">Tokens: {'{YYYY} {YY} {MM} {SEQ:n}'}. Preview: <b>{preview}</b></div>
            </div>
          )}

          {!codeLocked && (
            <div className="col-md-2"><button className="btn btn-outline-primary btn-sm" onClick={saveCode}>Save code rule</button></div>
          )}
          {codeLocked && <div className="col-md-5 small text-muted"><i className="bi bi-lock-fill me-1" />Locked: Basic Details is finalized.</div>}
        </div>
      </div>

      {sections.map((s) => (
        <div className="card mb-3" key={s.id}>
          <div className="card-header d-flex align-items-center gap-2 flex-wrap">
            <span className="fw-semibold">{s.title}</span>
            <span className={`badge ${s.isFinalized ? 'text-bg-danger' : 'text-bg-warning'}`}>
              <i className={`bi ${s.isFinalized ? 'bi-lock-fill' : 'bi-pencil'} me-1`} />{s.isFinalized ? 'Finalized' : 'Draft'}
            </span>
            {s.isMain && <span className="badge text-bg-light">Employee table</span>}
            {!s.isMain && <span className="badge text-bg-light">{s.isMandatory ? 'Mandatory' : 'Optional'}</span>}

            {!s.isFinalized && (
              <div className="ms-auto d-flex gap-2">
                <button className="btn btn-sm btn-outline-secondary" onClick={() => setSectionEdit(s)}><i className="bi bi-pencil me-1" />Section</button>
                <button className="btn btn-sm btn-primary" onClick={() => setFieldEdit({ sectionId: s.id, field: null })}><i className="bi bi-plus-lg me-1" />Field</button>
                <button className="btn btn-sm btn-danger" onClick={() => finalize(s)}><i className="bi bi-lock me-1" />Finalize</button>
                {!s.isMain && <button className="btn btn-sm btn-outline-danger" onClick={() => removeSection(s)}><i className="bi bi-trash" /></button>}
              </div>
            )}
          </div>

          {fieldEdit?.sectionId === s.id && !s.isFinalized && (
            <div className="p-3 pb-0">
              <FieldForm
                key={fieldEdit.field?.id ?? 'new'}
                sectionId={s.id}
                field={fieldEdit.field}
                onSaved={() => { setFieldEdit(null); load(); }}
                onCancel={() => setFieldEdit(null)}
              />
            </div>
          )}

          <div className="table-responsive">
            <table className="table table-sm align-middle mb-0">
              <thead className="table-light">
                <tr>
                  <th style={{ width: 80 }}>Order</th><th>Label</th><th>Column</th><th>Type</th><th>Rules</th><th style={{ width: 110 }} />
                </tr>
              </thead>
              <tbody>
                {s.fields.map((f, i) => (
                  <tr key={f.id}>
                    <td>
                      <button className="btn btn-sm btn-link p-0 me-1" disabled={s.isFinalized || i === 0} onClick={() => move(s, i, -1)}><i className="bi bi-arrow-up" /></button>
                      <button className="btn btn-sm btn-link p-0" disabled={s.isFinalized || i === s.fields.length - 1} onClick={() => move(s, i, 1)}><i className="bi bi-arrow-down" /></button>
                    </td>
                    <td>{f.label} {f.isCore && <i className="bi bi-lock-fill text-muted small" title="Base field" />}</td>
                    <td className="font-monospace small">{f.columnName}</td>
                    <td className="small">{f.dataType}</td>
                    <td className="small">
                      {f.isRequired && <span className="badge text-bg-light me-1">Required</span>}
                      {f.isUnique && <span className="badge text-bg-light me-1">Unique</span>}
                      {f.validationRegex && <span className="badge text-bg-light me-1" title={f.validationRegex}>Format</span>}
                      {Object.values(f.rules || {}).some((v) => v != null && v !== false && !(Array.isArray(v) && !v.length)) && <span className="badge text-bg-light me-1">Rules</span>}
                      {f.showInList && <span className="badge text-bg-info me-1">List</span>}
                      {f.isSearchable && <span className="badge text-bg-info">Search</span>}
                    </td>
                    <td className="text-end">
                      {!s.isFinalized && (
                        <>
                          <button className="btn btn-sm btn-outline-primary me-1" onClick={() => setFieldEdit({ sectionId: s.id, field: f })}><i className="bi bi-pencil" /></button>
                          {!f.isCore && <button className="btn btn-sm btn-outline-danger" onClick={() => removeField(f)}><i className="bi bi-trash" /></button>}
                        </>
                      )}
                    </td>
                  </tr>
                ))}
                {s.fields.length === 0 && <tr><td colSpan={6} className="text-center text-muted py-3">No fields yet.</td></tr>}
              </tbody>
            </table>
          </div>
        </div>
      ))}
    </div>
  );
}