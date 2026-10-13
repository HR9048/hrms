import { useEffect, useState } from 'react';
import Swal from 'sweetalert2';
import api from '../../api/axios';
import { useLanguage } from '../../context/LanguageContext';
import OrgTabs from '../../components/OrgTabs.jsx';
import usePageAccess from '../../hooks/usePageAccess';

const emptyNodeForm = {
  parentNodeId: '', nodeTypeId: '', nodeName: '', nodeCode: '', description: '',
  location: '', address: '', contactNumber: '', email: '', displayOrder: 0
};

function getApiError(err, fallback) {
  return err?.response?.data?.message || err?.response?.data?.error || fallback;
}

// ---------------------------------------------------------------
// Tree helpers (kept in this file so the page has no dependency on
// utils/orgTree.js)
// ---------------------------------------------------------------

function buildTree(flat) {
  const map = new Map();
  flat.forEach((n) => map.set(n.node_id, { ...n, children: [] }));

  const roots = [];
  map.forEach((n) => {
    const parent = n.parent_node_id != null ? map.get(n.parent_node_id) : null;
    if (parent) parent.children.push(n);
    else roots.push(n);
  });
  return roots;
}

function getDescendantIds(flat, nodeId) {
  const result = new Set();
  const queue = [nodeId];
  while (queue.length) {
    const current = queue.shift();
    flat.forEach((n) => {
      if (n.parent_node_id === current && !result.has(n.node_id)) {
        result.add(n.node_id);
        queue.push(n.node_id);
      }
    });
  }
  return result;
}

// Flattens the tree back into an ordered, indented list - used for the
// "Parent node" <select> so options read in hierarchy order.
function indentedOptions(nodes, depth = 0, out = []) {
  nodes.forEach((n) => {
    out.push({ node: n, depth });
    if (n.children?.length) indentedOptions(n.children, depth + 1, out);
  });
  return out;
}

// ---------------------------------------------------------------
// Bootstrap modal (React-controlled, no Bootstrap JS needed)
// ---------------------------------------------------------------

function BsModal({ title, subtitle, onClose, footer, busy, size, children }) {
  return (
    <div
      className="modal fade show d-block"
      tabIndex="-1"
      role="dialog"
      style={{ backgroundColor: 'rgba(0, 0, 0, 0.5)' }}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget && !busy) onClose();
      }}
    >
      <div className={`modal-dialog modal-dialog-centered modal-dialog-scrollable ${size || ''}`}>
        <div className="modal-content">
          <div className="modal-header">
            <div>
              <h5 className="modal-title">{title}</h5>
              {subtitle && <div className="small text-muted">{subtitle}</div>}
            </div>
            <button type="button" className="btn-close" onClick={onClose} disabled={busy} />
          </div>
          <div className="modal-body">{children}</div>
          {footer && <div className="modal-footer">{footer}</div>}
        </div>
      </div>
    </div>
  );
}

export default function OrganizationHierarchy() {
  const { t } = useLanguage();
  const [flatNodes, setFlatNodes] = useState([]);
  const [nodeTypes, setNodeTypes] = useState([]);
  const [loading, setLoading] = useState(true);
  const [expanded, setExpanded] = useState(new Set());
  const { permission, canView, canAdd, canEdit, canDelete, currentPath } = usePageAccess();

  const [modal, setModal] = useState(null); // 'create' | 'edit' | 'move' | 'history' | null
  const [activeNode, setActiveNode] = useState(null);
  const [nodeForm, setNodeForm] = useState(emptyNodeForm);
  const [moveTarget, setMoveTarget] = useState('');
  const [history, setHistory] = useState([]);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [saving, setSaving] = useState(false);

  // The organization lock covers this page too: while LOCKED or
  // EDIT_REQUESTED, the hierarchy is read-only. Locking / requesting edit
  // access / approving lives on the Organization Details page.
  const [lockStatus, setLockStatus] = useState('UNLOCKED');
  const isEditable = lockStatus === 'UNLOCKED' || lockStatus === 'APPROVED_EDIT';

  function loadLockStatus() {
    api.get('/organization/lock').then((res) => setLockStatus(res.data.status)).catch(() => {});
  }

  function load() {
    setLoading(true);
    Promise.all([api.get('/organization/hierarchy'), api.get('/organization/node-types')])
      .then(([hierarchyRes, typesRes]) => {
        setFlatNodes(Array.isArray(hierarchyRes.data) ? hierarchyRes.data : []);
        setNodeTypes(Array.isArray(typesRes.data) ? typesRes.data : []);
      })
      .catch((err) => {
        Swal.fire('Error', getApiError(err, 'Unable to load the organization hierarchy.'), 'error');
      })
      .finally(() => setLoading(false));

    loadLockStatus();
  }

  useEffect(load, []);

  const tree = buildTree(flatNodes);

  function toggleExpanded(nodeId) {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(nodeId)) next.delete(nodeId);
      else next.add(nodeId);
      return next;
    });
  }

  function expandAll() {
    const withChildren = new Set();
    flatNodes.forEach((n) => {
      if (n.parent_node_id != null) withChildren.add(n.parent_node_id);
    });
    setExpanded(withChildren);
  }

  function closeModal() {
    setModal(null);
    setActiveNode(null);
    setNodeForm(emptyNodeForm);
  }

  function openCreate(parentNode) {
    setNodeForm({ ...emptyNodeForm, parentNodeId: parentNode ? parentNode.node_id : '' });
    setActiveNode(null);
    setModal('create');
  }

  function openEdit(node) {
    setNodeForm({
      parentNodeId: node.parent_node_id ?? '',
      nodeTypeId: node.node_type_id,
      nodeName: node.node_name,
      nodeCode: node.node_code,
      description: node.description || '',
      location: node.location || '',
      address: node.address || '',
      contactNumber: node.contact_number || '',
      email: node.email || '',
      displayOrder: node.display_order
    });
    setActiveNode(node);
    setModal('edit');
  }

  function openMove(node) {
    setActiveNode(node);
    setMoveTarget(node.parent_node_id ?? '');
    setModal('move');
  }

  async function openHistory(node) {
    setActiveNode(node);
    setHistory([]);
    setModal('history');
    setHistoryLoading(true);
    try {
      const res = await api.get(`/organization/nodes/${node.node_id}/history`);
      setHistory(Array.isArray(res.data) ? res.data : []);
    } catch (err) {
      Swal.fire('Error', getApiError(err, 'Unable to load node history.'), 'error');
    } finally {
      setHistoryLoading(false);
    }
  }

  // ---------------------------------------------------------------
  // CREATE / EDIT
  // ---------------------------------------------------------------

  async function handleNodeSubmit(e) {
    e.preventDefault();

    const nodeName = nodeForm.nodeName.trim();
    const nodeCode = nodeForm.nodeCode.trim();
    const nodeTypeId = nodeForm.nodeTypeId;

    if (!nodeTypeId) {
      Swal.fire('Required', 'Please select a node type.', 'warning');
      return;
    }
    if (!nodeName) {
      Swal.fire('Required', 'Node name is required.', 'warning');
      return;
    }
    if (nodeName.length < 2) {
      Swal.fire('Invalid Name', 'Node name must contain at least 2 characters.', 'warning');
      return;
    }
    if (!nodeCode) {
      Swal.fire('Required', 'Node code is required.', 'warning');
      return;
    }
    if (!/^[A-Za-z0-9_-]+$/.test(nodeCode)) {
      Swal.fire('Invalid Code', 'Node code can contain only letters, numbers, hyphens and underscores.', 'warning');
      return;
    }
    if (nodeForm.email && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(nodeForm.email)) {
      Swal.fire('Invalid Email', 'Please enter a valid email address.', 'warning');
      return;
    }
    if (nodeForm.contactNumber && !/^[0-9+\-\s()]{7,20}$/.test(nodeForm.contactNumber)) {
      Swal.fire('Invalid Contact Number', 'Please enter a valid contact number.', 'warning');
      return;
    }
    if (Number(nodeForm.displayOrder) < 0) {
      Swal.fire('Invalid Order', 'Display order cannot be negative.', 'warning');
      return;
    }

    const isEdit = modal === 'edit' && activeNode;

    const confirm = await Swal.fire({
      title: isEdit ? 'Update node?' : 'Create node?',
      text: isEdit
        ? `Are you sure you want to update "${nodeName}"?`
        : `Are you sure you want to create "${nodeName}"?`,
      icon: 'question',
      showCancelButton: true,
      confirmButtonText: isEdit ? 'Update' : 'Create',
      cancelButtonText: 'Cancel'
    });

    if (!confirm.isConfirmed) return;

    const payload = {
      nodeTypeId: Number(nodeTypeId),
      nodeName,
      nodeCode,
      description: nodeForm.description.trim() || null,
      location: nodeForm.location.trim() || null,
      address: nodeForm.address.trim() || null,
      contactNumber: nodeForm.contactNumber.trim() || null,
      email: nodeForm.email.trim() || null,
      displayOrder: Number(nodeForm.displayOrder) || 0
    };

    setSaving(true);
    try {
      if (isEdit) {
        await api.put(`/organization/nodes/${activeNode.node_id}`, payload);
      } else {
        const parentNodeId = nodeForm.parentNodeId === '' ? null : Number(nodeForm.parentNodeId);
        await api.post('/organization/nodes', { ...payload, parentNodeId });

        // Keep the parent open so the new child is visible right away.
        if (parentNodeId != null) {
          setExpanded((prev) => new Set(prev).add(parentNodeId));
        }
      }

      closeModal();
      load();

      Swal.fire({
        icon: 'success',
        title: isEdit ? 'Node updated' : 'Node created',
        timer: 1500,
        showConfirmButton: false
      });
    } catch (err) {
      Swal.fire('Save Failed', getApiError(err, 'Something went wrong while saving the node.'), 'error');
      loadLockStatus();
    } finally {
      setSaving(false);
    }
  }

  // ---------------------------------------------------------------
  // MOVE
  // ---------------------------------------------------------------

  async function handleMoveSubmit(e) {
    e.preventDefault();

    if (!activeNode) {
      Swal.fire('Error', 'No node selected.', 'error');
      return;
    }

    if (moveTarget !== '' && Number(moveTarget) === Number(activeNode.node_id)) {
      Swal.fire('Invalid Move', 'A node cannot be moved under itself.', 'warning');
      return;
    }

    const selectedParent = moveTarget === '' ? null : Number(moveTarget);

    const confirm = await Swal.fire({
      title: 'Move node?',
      text:
        selectedParent === null
          ? `Move "${activeNode.node_name}" to the top level?`
          : `Move "${activeNode.node_name}" under the selected parent node?`,
      icon: 'question',
      showCancelButton: true,
      confirmButtonText: 'Move',
      cancelButtonText: 'Cancel'
    });

    if (!confirm.isConfirmed) return;

    setSaving(true);
    try {
      await api.put(`/organization/nodes/${activeNode.node_id}/move`, { newParentNodeId: selectedParent });

      if (selectedParent != null) {
        setExpanded((prev) => new Set(prev).add(selectedParent));
      }

      closeModal();
      load();

      Swal.fire({ icon: 'success', title: 'Node moved', timer: 1500, showConfirmButton: false });
    } catch (err) {
      Swal.fire('Move Failed', getApiError(err, 'Something went wrong while moving the node.'), 'error');
      loadLockStatus();
    } finally {
      setSaving(false);
    }
  }

  // ---------------------------------------------------------------
  // ACTIVATE / DEACTIVATE / DELETE
  // ---------------------------------------------------------------

  async function toggleStatus(node) {
    const nextStatus = node.status === 'ACTIVE' ? 'INACTIVE' : 'ACTIVE';
    const action = nextStatus === 'ACTIVE' ? 'activate' : 'deactivate';

    const confirm = await Swal.fire({
      title: `${action.charAt(0).toUpperCase()}${action.slice(1)} node?`,
      text: `Are you sure you want to ${action} "${node.node_name}"?`,
      icon: 'question',
      showCancelButton: true,
      confirmButtonText: 'Yes',
      cancelButtonText: 'Cancel'
    });

    if (!confirm.isConfirmed) return;

    try {
      await api.put(`/organization/nodes/${node.node_id}/status`, { status: nextStatus });
      load();
    } catch (err) {
      Swal.fire('Error', getApiError(err, `Unable to ${action} the node.`), 'error');
      loadLockStatus();
    }
  }

  // Delete is wired up but the button is hidden in the row (as in your
  // original file) - un-comment the button in OrgTreeRow to enable it.
  async function handleDelete(node) {
    const confirm = await Swal.fire({
      title: 'Delete node?',
      text: `"${node.node_name}" will be deleted. This action cannot be undone.`,
      icon: 'warning',
      showCancelButton: true,
      confirmButtonText: 'Delete',
      confirmButtonColor: '#dc3545',
      cancelButtonText: 'Cancel'
    });

    if (!confirm.isConfirmed) return;

    try {
      await api.delete(`/organization/nodes/${node.node_id}`);
      load();
    } catch (err) {
      Swal.fire('Error', getApiError(err, 'Something went wrong while deleting the node.'), 'error');
      loadLockStatus();
    }
  }

  // Options for the Move modal: every node except the one being moved and
  // any of its own descendants (would create a circular hierarchy).
  const moveOptions = activeNode
    ? (() => {
        const descendants = getDescendantIds(flatNodes, activeNode.node_id);
        return indentedOptions(tree).filter(
          ({ node }) => node.node_id !== activeNode.node_id && !descendants.has(node.node_id)
        );
      })()
    : [];

  // ---------------------------------------------------------------
  // RENDER
  // ---------------------------------------------------------------

  return (
    <div className="container-fluid py-3 bs-page">
      <div className="d-flex flex-wrap align-items-start justify-content-between gap-3 mb-3">
        <div>
          <h4 className="fw-semibold mb-1">{t('organizationHierarchy')}</h4>
          <p className="text-muted small mb-0">
            Build and manage your organization's structure — as many levels deep as you need.
          </p>
        </div>

        <div className="d-flex gap-2">
          {flatNodes.length > 0 && (
            <div className="btn-group">
              <button type="button" className="btn btn-outline-secondary" onClick={expandAll}>
                <i className="bi bi-arrows-expand me-1" />Expand all
              </button>
              <button type="button" className="btn btn-outline-secondary" onClick={() => setExpanded(new Set())}>
                <i className="bi bi-arrows-collapse me-1" />Collapse all
              </button>
            </div>
          )}
          
          {canAdd && (
            <button type="button" className="btn btn-primary" onClick={() => openCreate(null)} disabled={!isEditable}>
              <i className="bi bi-plus-lg me-1" />
              {t('createNode')}
            </button>
          )}
        </div>
      </div>

      <OrgTabs />

      {!isEditable && (
        <div className="alert alert-warning d-flex align-items-start gap-2 mt-3" role="alert">
          <i className="bi bi-lock-fill fs-5" />
          <div>
            <strong>Organization is locked.</strong> The hierarchy is read-only until an edit request is approved —
            manage that from Organization Details.
          </div>
        </div>
      )}

      <div className="card shadow-sm mt-3">
        <div className="card-body">
          {loading ? (
            <div className="d-flex align-items-center justify-content-center gap-2 text-muted py-4">
              <div className="spinner-border spinner-border-sm" role="status" />
              Loading...
            </div>
          ) : canAdd && tree.length === 0 ? (
            <div className="text-center text-muted py-5">
              <i className="bi bi-diagram-3 fs-1 d-block mb-2" />
              No nodes yet. Create the first one above.
            </div>
          ) : (
            tree.map((node) => (
              <OrgTreeRow
                key={node.node_id}
                node={node}
                expanded={expanded}
                editable={isEditable}
                onToggle={toggleExpanded}
                onCreateChild={openCreate}
                onEdit={openEdit}
                onMove={openMove}
                onToggleStatus={toggleStatus}
                onDelete={handleDelete}
                onHistory={openHistory}
                canAdd={canAdd}
                canEdit={canEdit}
                canDelete={canDelete}
              />
            ))
          )}
        </div>
      </div>

      {/* CREATE / EDIT MODAL */}
      {(modal === 'create' || modal === 'edit') && (
        <BsModal
          title={modal === 'edit' ? t('editNode') : t('createNode')}
          onClose={closeModal}
          busy={saving}
          size="modal-lg"
          footer={
            <>
              <button type="button" className="btn btn-light" onClick={closeModal} disabled={saving}>
                {t('cancel')}
              </button>
              <button type="submit" form="node-form" className="btn btn-primary" disabled={saving}>
                {saving ? (
                  <><span className="spinner-border spinner-border-sm me-2" />Saving...</>
                ) : (
                  <><i className="bi bi-save me-2" />{t('save')}</>
                )}
              </button>
            </>
          }
        >
          <form id="node-form" onSubmit={handleNodeSubmit}>
            <div className="row g-3">
              {modal === 'create' && (
                <div className="col-12">
                  <label className="form-label">{t('parentNode')}</label>
                  <select
                    className="form-select"
                    value={nodeForm.parentNodeId}
                    onChange={(e) => setNodeForm((f) => ({ ...f, parentNodeId: e.target.value }))}
                  >
                    <option value="">— Top level —</option>
                    {indentedOptions(tree).map(({ node, depth }) => (
                      <option key={node.node_id} value={node.node_id}>
                        {'— '.repeat(depth)}{node.node_name}
                      </option>
                    ))}
                  </select>
                </div>
              )}

              <div className="col-md-4">
                <label className="form-label">{t('nodeType')} <span className="text-danger">*</span></label>
                <select
                  className="form-select"
                  value={nodeForm.nodeTypeId}
                  onChange={(e) => setNodeForm((f) => ({ ...f, nodeTypeId: e.target.value }))}
                >
                  <option value="">Select...</option>
                  {nodeTypes.filter((nt) => nt.status === 'ACTIVE').map((nt) => (
                    <option key={nt.node_type_id} value={nt.node_type_id}>{nt.type_name}</option>
                  ))}
                </select>
              </div>

              <div className="col-md-5">
                <label className="form-label">{t('nodeName')} <span className="text-danger">*</span></label>
                <input
                  className="form-control"
                  value={nodeForm.nodeName}
                  onChange={(e) => setNodeForm((f) => ({ ...f, nodeName: e.target.value }))}
                />
              </div>

              <div className="col-md-3">
                <label className="form-label">{t('nodeCode')} <span className="text-danger">*</span></label>
                <input
                  className="form-control"
                  value={nodeForm.nodeCode}
                  onChange={(e) => setNodeForm((f) => ({ ...f, nodeCode: e.target.value }))}
                />
              </div>

              <div className="col-md-6">
                <label className="form-label">Location</label>
                <input
                  className="form-control"
                  value={nodeForm.location}
                  onChange={(e) => setNodeForm((f) => ({ ...f, location: e.target.value }))}
                />
              </div>

              <div className="col-md-6">
                <label className="form-label">Order</label>
                <input
                  type="number"
                  className="form-control"
                  value={nodeForm.displayOrder}
                  onChange={(e) => setNodeForm((f) => ({ ...f, displayOrder: e.target.value }))}
                />
              </div>

              <div className="col-md-6">
                <label className="form-label">Contact number</label>
                <input
                  className="form-control"
                  value={nodeForm.contactNumber}
                  onChange={(e) => setNodeForm((f) => ({ ...f, contactNumber: e.target.value }))}
                />
              </div>

              <div className="col-md-6">
                <label className="form-label">Email</label>
                <input
                  type="email"
                  className="form-control"
                  value={nodeForm.email}
                  onChange={(e) => setNodeForm((f) => ({ ...f, email: e.target.value }))}
                />
              </div>

              <div className="col-12">
                <label className="form-label">Address</label>
                <input
                  className="form-control"
                  value={nodeForm.address}
                  onChange={(e) => setNodeForm((f) => ({ ...f, address: e.target.value }))}
                />
              </div>

              <div className="col-12">
                <label className="form-label">Description</label>
                <input
                  className="form-control"
                  value={nodeForm.description}
                  onChange={(e) => setNodeForm((f) => ({ ...f, description: e.target.value }))}
                />
              </div>
            </div>
          </form>
        </BsModal>
      )}

      {/* MOVE MODAL */}
      {modal === 'move' && activeNode && (
        <BsModal
          title={`${t('moveNode')}: ${activeNode.node_name}`}
          onClose={closeModal}
          busy={saving}
          footer={
            <>
              <button type="button" className="btn btn-light" onClick={closeModal} disabled={saving}>
                {t('cancel')}
              </button>
              <button type="submit" form="move-form" className="btn btn-primary" disabled={saving}>
                {saving ? (
                  <><span className="spinner-border spinner-border-sm me-2" />Saving...</>
                ) : (
                  <><i className="bi bi-arrows-move me-2" />{t('save')}</>
                )}
              </button>
            </>
          }
        >
          <form id="move-form" onSubmit={handleMoveSubmit}>
            <label className="form-label">New {t('parentNode')}</label>
            <select className="form-select" value={moveTarget} onChange={(e) => setMoveTarget(e.target.value)}>
              <option value="">— Top level —</option>
              {moveOptions.map(({ node, depth }) => (
                <option key={node.node_id} value={node.node_id}>
                  {'— '.repeat(depth)}{node.node_name}
                </option>
              ))}
            </select>
            <div className="form-text mt-2">
              You can't move a node under itself or under one of its own sub-nodes.
            </div>
          </form>
        </BsModal>
      )}

      {/* HISTORY MODAL */}
      {modal === 'history' && activeNode && (
        <BsModal
          title={`${t('viewHistory')}: ${activeNode.node_name}`}
          onClose={closeModal}
          size="modal-lg"
          footer={
            <button type="button" className="btn btn-light" onClick={closeModal}>Close</button>
          }
        >
          {historyLoading ? (
            <div className="d-flex align-items-center justify-content-center gap-2 text-muted py-3">
              <div className="spinner-border spinner-border-sm" role="status" />
              Loading...
            </div>
          ) : history.length === 0 ? (
            <div className="text-center text-muted py-4">
              <i className="bi bi-clock-history fs-1 d-block mb-2" />
              No history recorded yet.
            </div>
          ) : (
            <div className="table-responsive">
              <table className="table table-sm align-middle mb-0">
                <thead className="table-light">
                  <tr>
                    <th>Action</th>
                    <th>Details</th>
                    <th>By</th>
                    <th>When</th>
                  </tr>
                </thead>
                <tbody>
                  {history.map((h) => (
                    <tr key={h.history_id}>
                      <td><span className="badge text-bg-secondary">{h.action_type}</span></td>
                      <td className="text-muted small">
                        {h.action_type === 'RENAME' && `${h.old_node_name} → ${h.new_node_name}`}
                        {h.action_type === 'MOVE' &&
                          `Parent #${h.old_parent_node_id ?? '—'} → #${h.new_parent_node_id ?? '—'}`}
                        {!['RENAME', 'MOVE'].includes(h.action_type) && (h.remarks || '—')}
                      </td>
                      <td>{h.first_name ? `${h.first_name} ${h.last_name || ''}`.trim() : '—'}</td>
                      <td className="small">{h.changed_at ? new Date(h.changed_at).toLocaleString() : '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </BsModal>
      )}
    </div>
  );
}

// ---------------------------------------------------------------
// One row of the tree (recursive)
// ---------------------------------------------------------------

function OrgTreeRow({ node, expanded, editable, onToggle, onCreateChild, onEdit, onMove, onToggleStatus, onDelete, onHistory, canAdd, canEdit, canDelete }) {
  const hasChildren = node.children && node.children.length > 0;
  const isOpen = expanded.has(node.node_id);
  const isActive = node.status === 'ACTIVE';

  return (
    <div>
      <div className={`d-flex align-items-center gap-2 border rounded bg-white p-2 mb-2 ${isActive ? '' : 'opacity-50'}`}>
        {hasChildren ? (
          <button
            type="button"
            className="btn btn-sm btn-link text-secondary p-0"
            style={{ width: 24 }}
            onClick={() => onToggle(node.node_id)}
            aria-label="Toggle children"
          >
            <i className={`bi ${isOpen ? 'bi-chevron-down' : 'bi-chevron-right'}`} />
          </button>
        ) : (
          <span style={{ width: 24 }} />
        )}

        <div className="flex-grow-1 d-flex flex-wrap align-items-center gap-2">
          <span className="fw-semibold">{node.node_name}</span>
          <span className="text-muted small">{node.node_code}</span>
          <span className="badge text-bg-secondary">{node.type_name}</span>
          <span className={`badge ${isActive ? 'text-bg-success' : 'text-bg-danger'}`}>{node.status}</span>
          {hasChildren && <span className="text-muted small">({node.children.length})</span>}
        </div>

        <div className="btn-group btn-group-sm">
          {canAdd && (
          <button
            type="button"
            className="btn btn-outline-primary"
            title="Add child node"
            onClick={() => onCreateChild(node)}
            disabled={!editable}
          >
            <i className="bi bi-plus-lg" />
          </button>
          )}
          {canEdit && (
          <button
            type="button"
            className="btn btn-outline-secondary"
            title="Edit"
            onClick={() => onEdit(node)}
            disabled={!editable}
          >
            <i className="bi bi-pencil" />
          </button>
          )}
          {canEdit && (
          <button
            type="button"
            className="btn btn-outline-secondary"
            title="Move"
            onClick={() => onMove(node)}
            disabled={!editable}
          >
            <i className="bi bi-arrows-move" />
          </button>
          )}
          {canDelete && (
          <button
            type="button"
            className={`btn ${isActive ? 'btn-outline-danger' : 'btn-outline-success'}`}
            title={isActive ? 'Deactivate' : 'Activate'}
            onClick={() => onToggleStatus(node)}
            disabled={!editable}
          >
            <i className="bi bi-power" />
          </button>
          )}
          <button type="button" className="btn btn-outline-secondary" title="History" onClick={() => onHistory(node)}>
            <i className="bi bi-clock-history" />
          </button>
          {/*
          <button type="button" className="btn btn-outline-danger" title="Delete" onClick={() => onDelete(node)} disabled={!editable}>
            <i className="bi bi-trash3" />
          </button>
          */}
        </div>
      </div>

      {hasChildren && isOpen && (
        <div className="ms-3 ps-3 border-start">
          {node.children.map((child) => (
            <OrgTreeRow
              key={child.node_id}
              node={child}
              expanded={expanded}
              editable={editable}
              onToggle={onToggle}
              onCreateChild={onCreateChild}
              onEdit={onEdit}
              onMove={onMove}
              onToggleStatus={onToggleStatus}
              onDelete={onDelete}
              onHistory={onHistory}
              canAdd={canAdd}
              canEdit={canEdit}
              canDelete={canDelete}
            />
          ))}
        </div>
      )}
    </div>
  );
}