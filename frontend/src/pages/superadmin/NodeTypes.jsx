import { useEffect, useState } from 'react';
import Swal from 'sweetalert2';
import api from '../../api/axios';
import { useLanguage } from '../../context/LanguageContext';
import OrgTabs from '../../components/OrgTabs.jsx';
import usePageAccess from '../../hooks/usePageAccess';

const emptyForm = { id: null, typeName: '', typeCode: '', description: '', displayOrder: 0 };

function getApiError(err, fallback) {
  return err?.response?.data?.message || err?.response?.data?.error || fallback;
}

export default function NodeTypes() {
  const { t } = useLanguage();
  const [types, setTypes] = useState([]);
  const [form, setForm] = useState(emptyForm);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const { permission, canView, canAdd, canEdit, canDelete, currentPath } = usePageAccess();
  // The organization lock covers this page too: while LOCKED or
  // EDIT_REQUESTED, node types are read-only. Locking / requesting edit
  // access / approving lives on the Organization Details page.
  const [lockStatus, setLockStatus] = useState('UNLOCKED');
  const isEditable = lockStatus === 'UNLOCKED' || lockStatus === 'APPROVED_EDIT';

  function loadLockStatus() {
    api.get('/organization/lock').then((res) => setLockStatus(res.data.status)).catch(() => {});
  }

  function load() {
    setLoading(true);
    api
      .get('/organization/node-types')
      .then((res) => setTypes(Array.isArray(res.data) ? res.data : []))
      .catch((err) => Swal.fire('Error', getApiError(err, 'Unable to load node types.'), 'error'))
      .finally(() => setLoading(false));
    loadLockStatus();
  }

  useEffect(load, []);

  function update(field, value) {
    setForm((f) => ({ ...f, [field]: value }));
  }

  function editType(nt) {
    setForm({
      id: nt.node_type_id,
      typeName: nt.type_name,
      typeCode: nt.type_code,
      description: nt.description || '',
      displayOrder: nt.display_order
    });
  }

  async function handleSubmit(e) {
    e.preventDefault();

    const typeName = form.typeName.trim();
    const typeCode = form.typeCode.trim();

    if (!typeName) {
      Swal.fire('Required', 'Node type name is required.', 'warning');
      return;
    }

    if (!typeCode) {
      Swal.fire('Required', 'Node type code is required.', 'warning');
      return;
    }

    if (!/^[A-Za-z0-9_-]+$/.test(typeCode)) {
      Swal.fire('Invalid Code', 'Code can contain only letters, numbers, hyphens and underscores.', 'warning');
      return;
    }

    if (Number(form.displayOrder) < 0) {
      Swal.fire('Invalid Order', 'Display order cannot be negative.', 'warning');
      return;
    }

    const payload = {
      typeName,
      typeCode,
      description: form.description.trim() || null,
      displayOrder: Number(form.displayOrder) || 0
    };

    setSaving(true);
    try {
      if (form.id) {
        await api.put(`/organization/node-types/${form.id}`, payload);
      } else {
        await api.post('/organization/node-types', payload);
      }

      Swal.fire({
        icon: 'success',
        title: form.id ? 'Node type updated' : 'Node type created',
        timer: 1500,
        showConfirmButton: false
      });

      setForm(emptyForm);
      load();
    } catch (err) {
      Swal.fire('Save Failed', getApiError(err, 'Something went wrong.'), 'error');
      loadLockStatus();
    } finally {
      setSaving(false);
    }
  }

  async function toggleStatus(nt) {
    const nextStatus = nt.status === 'ACTIVE' ? 'INACTIVE' : 'ACTIVE';
    const action = nextStatus === 'ACTIVE' ? 'activate' : 'deactivate';

    const confirm = await Swal.fire({
      title: `${action.charAt(0).toUpperCase()}${action.slice(1)} node type?`,
      text: `"${nt.type_name}" will be ${action}d.`,
      icon: 'question',
      showCancelButton: true,
      confirmButtonText: 'Yes',
      cancelButtonText: 'Cancel'
    });

    if (!confirm.isConfirmed) return;

    try {
      await api.put(`/organization/node-types/${nt.node_type_id}/status`, { status: nextStatus });
      load();
    } catch (err) {
      Swal.fire('Error', getApiError(err, `Unable to ${action} the node type.`), 'error');
      loadLockStatus();
    }
  }

  return (
    <div className="container-fluid py-3">
      <div className="mb-3">
        <h4 className="fw-semibold mb-1">{t('nodeTypeManagement')}</h4>
        <p className="text-muted small mb-0">
          Define the levels used in your hierarchy — Region, Division, Depot, Department, whatever fits your
          organization. Nothing here is hard-coded; the hierarchy view loads these dynamically.
        </p>
      </div>

      <OrgTabs />

      {!isEditable && (
        <div className="alert alert-warning d-flex align-items-start gap-2 mt-3" role="alert">
          <i className="bi bi-lock-fill fs-5" />
          <div>
            <strong>Organization is locked.</strong> Node types are read-only until an edit request is approved —
            manage that from Organization Details.
          </div>
        </div>
      )}

      <div className="row g-3 mt-1">
        {/* FORM */}
        <div className="col-lg-4">
          <form className="card shadow-sm" onSubmit={handleSubmit}>
            <fieldset disabled={!isEditable || saving} style={{ border: 0, margin: 0, padding: 0 }}>
              <div className="card-body">
                <h6 className="fw-semibold mb-3">{form.id ? t('editNode') : 'Create node type'}</h6>

                <div className="mb-3">
                  <label className="form-label">{t('nodeType')} name <span className="text-danger">*</span></label>
                  <input
                    className="form-control"
                    value={form.typeName}
                    onChange={(e) => update('typeName', e.target.value)}
                    placeholder="Region"
                  />
                </div>

                <div className="mb-3">
                  <label className="form-label">Code (unique) <span className="text-danger">*</span></label>
                  <input
                    className="form-control"
                    value={form.typeCode}
                    onChange={(e) => update('typeCode', e.target.value)}
                    placeholder="REGION"
                  />
                </div>

                <div className="mb-3">
                  <label className="form-label">Description</label>
                  <input
                    className="form-control"
                    value={form.description}
                    onChange={(e) => update('description', e.target.value)}
                  />
                </div>

                <div>
                  <label className="form-label">Order</label>
                  <input
                    type="number"
                    className="form-control"
                    value={form.displayOrder}
                    onChange={(e) => update('displayOrder', e.target.value)}
                  />
                </div>
              </div>

              <div className="card-footer bg-white d-flex justify-content-end gap-2">
                {form.id && (
                  <button type="button" className="btn btn-light" onClick={() => setForm(emptyForm)}>
                    {t('cancel')}
                  </button>
                )}
                {(canAdd || canEdit) && (
                <button type="submit" className="btn btn-primary">
                  {saving  ? (
                    <><span className="spinner-border spinner-border-sm me-2" />Saving...</>
                  ) : form.id ? (
                    <><i className="bi bi-save me-2" />{t('save')}</>
                  ) : !form.id ? (
                    <><i className="bi bi-save me-2" />{t('save')}</>
                  ) : <>NA</>}
                </button>
                )}

              </div>
            </fieldset>
          </form>
        </div>

        {/* LIST */}
        <div className="col-lg-8">
          <div className="card shadow-sm">
            <div className="card-body">
              <h6 className="fw-semibold mb-3">Existing node types</h6>

              {loading ? (
                <div className="d-flex align-items-center gap-2 text-muted py-3">
                  <div className="spinner-border spinner-border-sm" role="status" />
                  Loading...
                </div>
              ) : types.length === 0 ? (
                <div className="text-center text-muted py-5">
                  <i className="bi bi-layers fs-1 d-block mb-2" />
                  No node types yet. Create your first one on the left.
                </div>
              ) : (
                <div className="table-responsive">
                  <table className="table table-hover align-middle mb-0">
                    <thead className="table-light">
                      <tr>
                        <th>{t('nodeType')}</th>
                        <th>Code</th>
                        <th>Order</th>
                        <th>Status</th>
                        <th className="text-end">Actions</th>
                      </tr>
                    </thead>
                    <tbody>
                      {types.map((nt) => (
                        <tr key={nt.node_type_id}>
                          <td className="fw-semibold">{nt.type_name}</td>
                          <td><code>{nt.type_code}</code></td>
                          <td>{nt.display_order}</td>
                          <td>
                            <span className={`badge ${nt.status === 'ACTIVE' ? 'text-bg-success' : 'text-bg-secondary'}`}>
                              {nt.status}
                            </span>
                          </td>
                          <td className="text-end">
                            <div className="btn-group btn-group-sm">
                              {canEdit && (
                              <button
                                type="button"
                                className="btn btn-outline-primary"
                                onClick={() => editType(nt)}
                                disabled={!isEditable}
                              >
                                <i className="bi bi-pencil me-1" />Edit
                              </button>
                              )}
                              {canDelete && (
                              <button
                                type="button"
                                className={`btn ${nt.status === 'ACTIVE' ? 'btn-outline-danger' : 'btn-outline-success'}`}
                                onClick={() => toggleStatus(nt)}
                                disabled={!isEditable}
                              >
                                <i className="bi bi-power me-1" />
                                {nt.status === 'ACTIVE' ? t('deactivate') : t('activate')}
                              </button>
                              )}
                            </div> 
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}