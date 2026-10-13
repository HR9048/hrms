import { useEffect, useState } from 'react';
import Swal from 'sweetalert2';
import api from '../../api/axios';
import { useLanguage } from '../../context/LanguageContext';
import OrgTabs from '../../components/OrgTabs.jsx';
import usePageAccess from '../../hooks/usePageAccess';


const emptyForm = {
  organizationName: '', organizationCode: '', logo: '', address: '',
  contactNumber: '', email: '', status: 'ACTIVE'
};

const emptyRequestForm = { jobTitleId: '', userId: '', reason: '' };

const LOCK_BADGES = {
  UNLOCKED: { className: 'text-bg-success', icon: 'bi-unlock-fill', label: 'Unlocked' },
  LOCKED: { className: 'text-bg-danger', icon: 'bi-lock-fill', label: 'Locked' },
  EDIT_REQUESTED: { className: 'text-bg-warning', icon: 'bi-clock-history', label: 'Edit Request Pending' },
  APPROVED_EDIT: { className: 'text-bg-info', icon: 'bi-key-fill', label: 'Edit Permission Granted' }
};

function getApiError(err, fallback) {
  return err?.response?.data?.message || err?.response?.data?.error || fallback;
}

export default function OrganizationDetails() {
  const { t } = useLanguage();

  const [org, setOrg] = useState(null);
  const [form, setForm] = useState(emptyForm);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const { permission, canView, canAdd, canEdit, canDelete, currentPath } = usePageAccess();
  // ---------------------------------------------------------
  // LOCK / EDIT-REQUEST state
  // ---------------------------------------------------------
  const [lockStatus, setLockStatus] = useState('UNLOCKED');
  const [lockLoading, setLockLoading] = useState(false);

  const [showRequestModal, setShowRequestModal] = useState(false);
  const [jobTitles, setJobTitles] = useState([]);
  const [users, setUsers] = useState([]);
  const [requestForm, setRequestForm] = useState(emptyRequestForm);
  const [requestLoading, setRequestLoading] = useState(false);

  const [pendingApprovals, setPendingApprovals] = useState([]);
  const [showApprovals, setShowApprovals] = useState(false);
  const [approvalActionId, setApprovalActionId] = useState(null);

  const isEditable = lockStatus === 'UNLOCKED' || lockStatus === 'APPROVED_EDIT';
  const isLocked = lockStatus === 'LOCKED' || lockStatus === 'EDIT_REQUESTED';
  const canRequestEdit = lockStatus === 'LOCKED';
  const badge = LOCK_BADGES[lockStatus] || LOCK_BADGES.UNLOCKED;

  // ---------------------------------------------------------
  // LOAD
  // ---------------------------------------------------------

  function load() {
    setLoading(true);
    api
      .get('/organization')
      .then((res) => {
        setOrg(res.data);
        setForm({
          organizationName: res.data.organization_name || '',
          organizationCode: res.data.organization_code || '',
          logo: res.data.logo || '',
          address: res.data.address || '',
          contactNumber: res.data.contact_number || '',
          email: res.data.email || '',
          status: res.data.status || 'ACTIVE'
        });
      })
      .catch(() => setOrg(null))
      .finally(() => setLoading(false));

    loadLockStatus();
    loadPendingApprovals();
  }

  function loadLockStatus() {
    api.get('/organization/lock').then((res) => setLockStatus(res.data.status)).catch(() => {});
  }

  function loadPendingApprovals() {
    api.get('/organization/edit-requests').then((res) => setPendingApprovals(res.data)).catch(() => {});
  }

  useEffect(load, []);

  function update(field, value) {
    setForm((f) => ({ ...f, [field]: value }));
  }

  // ---------------------------------------------------------
  // SAVE DETAILS
  // ---------------------------------------------------------

  async function handleSubmit(e) {
    e.preventDefault();

    if (!form.organizationName.trim() || !form.organizationCode.trim()) {
      Swal.fire('Required', 'Organization name and code are required.', 'warning');
      return;
    }

    if (form.email && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.email)) {
      Swal.fire('Invalid Email', 'Please enter a valid email address.', 'warning');
      return;
    }

    const confirm = await Swal.fire({
      title: 'Save changes?',
      text:
        lockStatus === 'APPROVED_EDIT'
          ? 'Saving will use up your one-time edit permission and lock the organization details again.'
          : 'The organization details will be updated.',
      icon: 'question',
      showCancelButton: true,
      confirmButtonText: 'Save',
      cancelButtonText: 'Cancel'
    });

    if (!confirm.isConfirmed) return;

    setSaving(true);
    try {
      const res = await api.put(`/organization/${org.organization_id}`, form);
      await Swal.fire('Saved', res.data?.message || 'Organization details updated.', 'success');
      load();
    } catch (err) {
      Swal.fire('Save Failed', getApiError(err, 'Something went wrong.'), 'error');
      loadLockStatus();
    } finally {
      setSaving(false);
    }
  }

  // ---------------------------------------------------------
  // LOCK
  // ---------------------------------------------------------

  async function handleLock() {
    const confirm = await Swal.fire({
      title: 'Lock organization?',
      text: 'Organization details, node types and hierarchy nodes will all become read-only until an edit request is approved.',
      icon: 'warning',
      showCancelButton: true,
      confirmButtonText: 'Lock',
      cancelButtonText: 'Cancel'
    });

    if (!confirm.isConfirmed) return;

    setLockLoading(true);
    try {
      await api.post('/organization/lock');
      await Swal.fire('Locked', 'Organization has been locked.', 'success');
      loadLockStatus();
    } catch (err) {
      Swal.fire('Error', getApiError(err, 'Unable to lock the organization.'), 'error');
    } finally {
      setLockLoading(false);
    }
  }

  // ---------------------------------------------------------
  // REQUEST EDIT
  // ---------------------------------------------------------

  async function openRequestModal() {
    setRequestForm(emptyRequestForm);
    setUsers([]);

    try {
      setRequestLoading(true);
      const res = await api.get('/organization-data-flow/request/job-titles');
      setJobTitles(res.data);
      setShowRequestModal(true);
    } catch (err) {
      Swal.fire('Error', getApiError(err, 'Unable to load job titles.'), 'error');
    } finally {
      setRequestLoading(false);
    }
  }

  async function handleJobTitleChange(value) {
    setRequestForm((f) => ({ ...f, jobTitleId: value, userId: '' }));
    setUsers([]);

    if (!value) return;

    try {
      setRequestLoading(true);
      const res = await api.get('/organization-data-flow/request/users', { params: { jobTitleId: value } });
      setUsers(res.data);
    } catch (err) {
      Swal.fire('Error', getApiError(err, 'Unable to load users.'), 'error');
    } finally {
      setRequestLoading(false);
    }
  }

  async function handleSendRequest(e) {
    e.preventDefault();

    if (!requestForm.jobTitleId) {
      Swal.fire('Required', 'Please select a job title.', 'warning');
      return;
    }
    if (!requestForm.userId) {
      Swal.fire('Required', 'Please select a user.', 'warning');
      return;
    }
    if (!requestForm.reason.trim()) {
      Swal.fire('Required', 'Please enter the reason for the edit request.', 'warning');
      return;
    }

    setRequestLoading(true);
    try {
      await api.post('/organization/edit-request', {
        jobTitleId: Number(requestForm.jobTitleId),
        requestedToUserId: Number(requestForm.userId),
        reason: requestForm.reason.trim()
      });
      setShowRequestModal(false);
      await Swal.fire('Request Sent', 'Edit request has been sent successfully.', 'success');
      loadLockStatus();
    } catch (err) {
      Swal.fire('Request Failed', getApiError(err, 'Unable to send edit request.'), 'error');
    } finally {
      setRequestLoading(false);
    }
  }

  // ---------------------------------------------------------
  // APPROVE / REJECT INCOMING REQUESTS
  // ---------------------------------------------------------

  async function handleApprove(request) {
    const confirm = await Swal.fire({
      title: 'Approve edit request?',
      text: `${request.requested_by_name || 'This user'} will be allowed to edit the organization.`,
      icon: 'question',
      showCancelButton: true,
      confirmButtonText: 'Approve',
      cancelButtonText: 'Cancel'
    });

    if (!confirm.isConfirmed) return;

    setApprovalActionId(request.id);
    try {
      await api.post(`/organization/edit-request/${request.id}/approve`);
      setPendingApprovals((prev) => prev.filter((r) => r.id !== request.id));
      Swal.fire('Approved', 'Edit permission granted.', 'success');
      loadLockStatus();
    } catch (err) {
      Swal.fire('Error', getApiError(err, 'Unable to approve this request.'), 'error');
    } finally {
      setApprovalActionId(null);
    }
  }

  async function handleReject(request) {
    const confirm = await Swal.fire({
      title: 'Reject edit request?',
      text: `${request.requested_by_name || 'This user'}'s request will be rejected.`,
      icon: 'warning',
      showCancelButton: true,
      confirmButtonText: 'Reject',
      confirmButtonColor: '#dc3545',
      cancelButtonText: 'Cancel'
    });

    if (!confirm.isConfirmed) return;

    setApprovalActionId(request.id);
    try {
      await api.post(`/organization/edit-request/${request.id}/reject`);
      setPendingApprovals((prev) => prev.filter((r) => r.id !== request.id));
      Swal.fire('Rejected', 'Edit request has been rejected.', 'success');
    } catch (err) {
      Swal.fire('Error', getApiError(err, 'Unable to reject this request.'), 'error');
    } finally {
      setApprovalActionId(null);
    }
  }

  // ---------------------------------------------------------
  // RENDER
  // ---------------------------------------------------------

  if (loading) {
    return (
      <div className="d-flex align-items-center justify-content-center gap-2 text-muted py-5">
        <div className="spinner-border spinner-border-sm" role="status" />
        <span>Loading...</span>
      </div>
    );
  }

  return (
    <div className="container-fluid py-3">

      {/* HEADER */}
      <div className="d-flex flex-wrap align-items-start justify-content-between gap-3 mb-3">
        <div>
          <h4 className="fw-semibold mb-1">{t('organizationDetails')}</h4>
          <p className="text-muted small mb-0">
            Your organization's core details. This was set up once during first deployment.
          </p>
        </div>

        <div className="d-flex flex-wrap align-items-center gap-2">
          {canEdit && (
          <button
            type="button"
            className="btn btn-outline-secondary btn-sm position-relative"
            onClick={() => setShowApprovals(true)}
            title="Edit requests waiting for your approval"
          >
            <i className="bi bi-bell me-1" />
            Approvals
            {pendingApprovals.length > 0 && (
              <span className="position-absolute top-0 start-100 translate-middle badge rounded-pill bg-danger">
                {pendingApprovals.length}
              </span>
            )}
          </button>
          )}

          <span className={`badge rounded-pill ${badge.className} px-3 py-2`}>
            <i className={`bi ${badge.icon} me-1`} />
            {badge.label}
          </span>

          {canEdit && org && isEditable && (
            <button type="button" className="btn btn-outline-danger btn-sm" onClick={handleLock} disabled={lockLoading}>
              {lockLoading ? (
                <span className="spinner-border spinner-border-sm me-1" />
              ) : (
                <i className="bi bi-lock me-1" />
              )}
              Lock
            </button>
          )}

          {canEdit && org && canRequestEdit && (
            <button type="button" className="btn btn-warning btn-sm" onClick={openRequestModal} disabled={requestLoading}>
              <i className="bi bi-key me-1" />
              Request Edit
            </button>
          )}
        </div>
      </div>

      <OrgTabs />

      {/* STATUS BANNERS */}
      {isLocked && (
        <div className="alert alert-warning d-flex align-items-start gap-2 mt-3" role="alert">
          <i className="bi bi-lock-fill fs-5" />
          <div>
            <strong>Organization is locked.</strong>{' '}
            {lockStatus === 'LOCKED'
              ? 'Organization details, node types and hierarchy nodes are read-only. Request edit permission from an authorized user to make changes.'
              : 'Your edit request is waiting for approval.'}
          </div>
        </div>
      )}

      {lockStatus === 'APPROVED_EDIT' && (
        <div className="alert alert-success d-flex align-items-start gap-2 mt-3" role="alert">
          <i className="bi bi-key-fill fs-5" />
          <div>
            <strong>You have edit permission.</strong> Saving the organization details uses this permission and
            locks everything again. Node types and hierarchy nodes stay editable until you press{' '}
            <strong>Lock</strong>.
          </div>
        </div>
      )}

      {/* DETAILS FORM */}
      {!org ? (
        <div className="card shadow-sm mt-3">
          <div className="card-body text-center py-5 text-muted">
            <i className="bi bi-building fs-1 d-block mb-2" />
            No organization found yet.
          </div>
        </div>
      ) : (
        <form className="card shadow-sm mt-3" onSubmit={handleSubmit}>
          <fieldset disabled={!isEditable || saving} style={{ border: 0, margin: 0, padding: 0 }}>
            <div className="card-body">
              <h6 className="fw-semibold mb-3">
                {t('organizationName')} &amp; {t('organizationCode')}
              </h6>

              <div className="row g-3">
                <div className="col-md-6">
                  <label className="form-label">{t('organizationName')} <span className="text-danger">*</span></label>
                  <input
                    className="form-control"
                    required
                    value={form.organizationName}
                    onChange={(e) => update('organizationName', e.target.value)}
                  />
                </div>

                <div className="col-md-6">
                  <label className="form-label">{t('organizationCode')} <span className="text-danger">*</span></label>
                  <input
                    className="form-control"
                    required
                    value={form.organizationCode}
                    onChange={(e) => update('organizationCode', e.target.value)}
                  />
                </div>

                <div className="col-md-6">
                  <label className="form-label">Logo URL</label>
                  <input
                    className="form-control"
                    value={form.logo}
                    onChange={(e) => update('logo', e.target.value)}
                  />
                </div>

                <div className="col-md-6">
                  <label className="form-label">Status</label>
                  <select
                    className="form-select"
                    value={form.status}
                    onChange={(e) => update('status', e.target.value)}
                  >
                    <option value="ACTIVE">Active</option>
                    <option value="INACTIVE">Inactive</option>
                  </select>
                </div>

                <div className="col-md-6">
                  <label className="form-label">Contact number</label>
                  <input
                    className="form-control"
                    value={form.contactNumber}
                    onChange={(e) => update('contactNumber', e.target.value)}
                  />
                </div>

                <div className="col-md-6">
                  <label className="form-label">Email</label>
                  <input
                    type="email"
                    className="form-control"
                    value={form.email}
                    onChange={(e) => update('email', e.target.value)}
                  />
                </div>

                <div className="col-12">
                  <label className="form-label">Address</label>
                  <input
                    className="form-control"
                    value={form.address}
                    onChange={(e) => update('address', e.target.value)}
                  />
                </div>
              </div>
            </div>

            <div className="card-footer bg-white d-flex justify-content-end">
              <button type="submit" className="btn btn-primary" disabled={saving || !isEditable || !canEdit}>
                {saving ? (
                  <><span className="spinner-border spinner-border-sm me-2" />Saving...</>
                ) : (
                  <><i className="bi bi-save me-2" />{t('save')}</>
                )}
              </button>
            </div>
          </fieldset>
        </form>
      )}

      {/* REQUEST EDIT MODAL */}
      {showRequestModal && (
        <div
          className="modal fade show d-block"
          tabIndex="-1"
          role="dialog"
          style={{ backgroundColor: 'rgba(0, 0, 0, 0.5)' }}
          onMouseDown={(e) => {
            if (e.target === e.currentTarget && !requestLoading) setShowRequestModal(false);
          }}
        >
          <div className="modal-dialog modal-dialog-centered">
            <div className="modal-content">
              <form onSubmit={handleSendRequest}>
                <div className="modal-header">
                  <div>
                    <h5 className="modal-title">Request Edit Permission</h5>
                    <div className="small text-muted">Choose who should approve your request.</div>
                  </div>
                  <button
                    type="button"
                    className="btn-close"
                    onClick={() => setShowRequestModal(false)}
                    disabled={requestLoading}
                  />
                </div>

                <div className="modal-body">
                  <div className="mb-3">
                    <label className="form-label">Request To Job Title <span className="text-danger">*</span></label>
                    <select
                      className="form-select"
                      value={requestForm.jobTitleId}
                      onChange={(e) => handleJobTitleChange(e.target.value)}
                      disabled={requestLoading}
                    >
                      <option value="">Select Job Title</option>
                      {jobTitles.map((jt) => (
                        <option key={jt.id} value={jt.id}>{jt.name}</option>
                      ))}
                    </select>
                  </div>

                  <div className="mb-3">
                    <label className="form-label">Request To User <span className="text-danger">*</span></label>
                    <select
                      className="form-select"
                      value={requestForm.userId}
                      onChange={(e) => setRequestForm((f) => ({ ...f, userId: e.target.value }))}
                      disabled={requestLoading || !requestForm.jobTitleId}
                    >
                      <option value="">Select User</option>
                      {users.map((u) => (
                        <option key={u.id} value={u.id}>
                          {u.name}{u.username ? ` (${u.username})` : ''}
                        </option>
                      ))}
                    </select>
                  </div>

                  <div>
                    <label className="form-label">Reason <span className="text-danger">*</span></label>
                    <textarea
                      className="form-control"
                      rows={4}
                      value={requestForm.reason}
                      onChange={(e) => setRequestForm((f) => ({ ...f, reason: e.target.value }))}
                      placeholder="Why do you need to edit the organization?"
                      disabled={requestLoading}
                    />
                  </div>
                </div>

                <div className="modal-footer">
                  <button
                    type="button"
                    className="btn btn-light"
                    onClick={() => setShowRequestModal(false)}
                    disabled={requestLoading}
                  >
                    {t('cancel')}
                  </button>
                  <button type="submit" className="btn btn-primary" disabled={requestLoading}>
                    {requestLoading ? (
                      <><span className="spinner-border spinner-border-sm me-2" />Sending...</>
                    ) : (
                      <><i className="bi bi-send me-2" />Send Request</>
                    )}
                  </button>
                </div>
              </form>
            </div>
          </div>
        </div>
      )}

      {/* INCOMING APPROVALS MODAL */}
      {showApprovals && (
        <div
          className="modal fade show d-block"
          tabIndex="-1"
          role="dialog"
          style={{ backgroundColor: 'rgba(0, 0, 0, 0.5)' }}
          onMouseDown={(e) => {
            if (e.target === e.currentTarget) setShowApprovals(false);
          }}
        >
          <div className="modal-dialog modal-dialog-centered modal-dialog-scrollable">
            <div className="modal-content">
              <div className="modal-header">
                <div>
                  <h5 className="modal-title">Edit Requests Awaiting Your Approval</h5>
                  <div className="small text-muted">Approve or reject requests sent to you.</div>
                </div>
                <button type="button" className="btn-close" onClick={() => setShowApprovals(false)} />
              </div>

              <div className="modal-body">
                {pendingApprovals.length === 0 ? (
                  <div className="text-center text-muted py-4">
                    <i className="bi bi-check2-circle fs-1 d-block mb-2" />
                    No pending requests right now.
                  </div>
                ) : (
                  <ul className="list-group list-group-flush">
                    {pendingApprovals.map((request) => {
                      const busy = approvalActionId === request.id;
                      return (
                        <li key={request.id} className="list-group-item px-0">
                          <div className="d-flex align-items-start gap-3">
                            <div
                              className="rounded-circle bg-primary-subtle text-primary d-flex align-items-center justify-content-center flex-shrink-0"
                              style={{ width: 38, height: 38 }}
                            >
                              <i className="bi bi-person" />
                            </div>

                            <div className="flex-grow-1">
                              <div className="fw-semibold">{request.requested_by_name || 'Unknown user'}</div>
                              {request.job_title_name && (
                                <div className="small text-muted">Requested as {request.job_title_name}</div>
                              )}
                              {request.reason && (
                                <div className="bg-light rounded p-2 small mt-2">{request.reason}</div>
                              )}
                              {request.created_at && (
                                <div className="small text-muted mt-1">
                                  {new Date(request.created_at).toLocaleString()}
                                </div>
                              )}

                              <div className="d-flex justify-content-end gap-2 mt-2">
                                <button
                                  type="button"
                                  className="btn btn-sm btn-outline-danger"
                                  disabled={busy}
                                  onClick={() => handleReject(request)}
                                >
                                  <i className="bi bi-x-lg me-1" />
                                  Reject
                                </button>
                                <button
                                  type="button"
                                  className="btn btn-sm btn-success"
                                  disabled={busy}
                                  onClick={() => handleApprove(request)}
                                >
                                  {busy ? (
                                    <span className="spinner-border spinner-border-sm me-1" />
                                  ) : (
                                    <i className="bi bi-check-lg me-1" />
                                  )}
                                  Approve
                                </button>
                              </div>
                            </div>
                          </div>
                        </li>
                      );
                    })}
                  </ul>
                )}
              </div>

              <div className="modal-footer">
                <button type="button" className="btn btn-light" onClick={() => setShowApprovals(false)}>
                  Close
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}