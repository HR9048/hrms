import { useEffect, useMemo, useState } from 'react';
import Swal from 'sweetalert2';
import api from '../../api/axios';
import './OrganizationDataFlowHierarchy.css';
import usePageAccess from '../../hooks/usePageAccess'; 

const STATUS = {
    DRAFT: 'DRAFT',
    UNLOCKED: 'UNLOCKED',
    LOCKED: 'LOCKED',
    EDIT_REQUESTED: 'EDIT_REQUESTED',
    APPROVED_EDIT: 'APPROVED_EDIT'
};

const NODE_TYPE_ICONS = {
    REGION: 'bi-map',
    ZONE: 'bi-map',
    DIVISION: 'bi-diagram-3',
    DEPOT: 'bi-building-gear',
    BRANCH: 'bi-building',
    UNIT: 'bi-building',
    DEFAULT: 'bi-building'
};

function getNodeIcon(nodeType) {
    if (!nodeType) return NODE_TYPE_ICONS.DEFAULT;
    return NODE_TYPE_ICONS[nodeType.toUpperCase()] || NODE_TYPE_ICONS.DEFAULT;
}

function getApiError(error, fallback) {
    return (
        error?.response?.data?.message ||
        error?.response?.data?.error ||
        fallback
    );
}

export default function OrganizationDataFlowHierarchy() {
    const { permission, canView, canAdd, canEdit, canDelete, currentPath } = usePageAccess();
    const [tree, setTree] = useState([]);
    const [availableNodes, setAvailableNodes] = useState([]);

    const [status, setStatus] = useState(STATUS.DRAFT);
    const [version, setVersion] = useState(1);

    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);

    const [showRequestModal, setShowRequestModal] = useState(false);

    const [jobTitles, setJobTitles] = useState([]);
    const [users, setUsers] = useState([]);

    const [selectedJobTitle, setSelectedJobTitle] = useState('');
    const [selectedUser, setSelectedUser] = useState('');
    const [requestReason, setRequestReason] = useState('');

    const [requestLoading, setRequestLoading] = useState(false);

    const [draggedNode, setDraggedNode] = useState(null);

    // Node picker modal: { mode: 'root' | 'child', parentNode, nodes, title, subtitle }
    const [pickerConfig, setPickerConfig] = useState(null);

    // Incoming edit-request approvals (requests sent TO the current user)
    const [pendingApprovals, setPendingApprovals] = useState([]);
    const [showApprovals, setShowApprovals] = useState(false);
    const [approvalsLoading, setApprovalsLoading] = useState(false);
    const [approvalActionId, setApprovalActionId] = useState(null);

    /* ============================= PERMISSIONS ============================= */

    const isEditable =
        status === STATUS.DRAFT ||
        status === STATUS.UNLOCKED ||
        status === STATUS.APPROVED_EDIT;

    const isLocked =
        status === STATUS.LOCKED ||
        status === STATUS.EDIT_REQUESTED;

    const canRequestEdit = status === STATUS.LOCKED;
    const canSave = isEditable;

    /* ============================= LOAD DATA ============================= */

    useEffect(() => {
        loadDataFlow();
        loadPendingApprovals();
    }, []);

    const loadPendingApprovals = async () => {
        try {
            const response = await api.get('/organization-data-flow/edit-requests');
            setPendingApprovals(Array.isArray(response.data) ? response.data : []);
        } catch (error) {
            console.error('Load Pending Approvals Error:', error);
        }
    };

    const loadDataFlow = async () => {
        try {
            setLoading(true);

            const response = await api.get('/organization-data-flow');
            const data = response.data;

            setTree(Array.isArray(data.tree) ? data.tree : []);
            setStatus(data.status || STATUS.DRAFT);
            setVersion(data.versionNo || 1);

            if (Array.isArray(data.availableNodes)) {
                setAvailableNodes(data.availableNodes);
            } else {
                await loadAvailableNodes();
            }
        } catch (error) {
            console.error('Load Organization Data Flow Error:', error);
            Swal.fire('Error', getApiError(error, 'Unable to load Organization Data Flow Hierarchy.'), 'error');
        } finally {
            setLoading(false);
        }
    };

    const loadAvailableNodes = async () => {
        try {
            const response = await api.get('/organization-data-flow/available-nodes');
            setAvailableNodes(Array.isArray(response.data) ? response.data : []);
        } catch (error) {
            console.error('Load Available Nodes Error:', error);
        }
    };

    /* ============================= TREE HELPERS ============================= */

    const flattenTree = (nodes, result = []) => {
        nodes.forEach(node => {
            result.push(node);
            if (node.children?.length) flattenTree(node.children, result);
        });
        return result;
    };

    const treeNodes = useMemo(() => flattenTree(tree), [tree]);

    const isDescendant = (node, id) => {
        if (!node.children?.length) return false;
        for (const child of node.children) {
            if (Number(child.id) === Number(id)) return true;
            if (isDescendant(child, id)) return true;
        }
        return false;
    };

    const removeNodeFromTree = (nodes, id) => {
        let removedNode = null;

        const newNodes = nodes
            .filter(node => {
                if (Number(node.id) === Number(id)) {
                    removedNode = node;
                    return false;
                }
                return true;
            })
            .map(node => {
                if (node.children?.length) {
                    const result = removeNodeFromTree(node.children, id);
                    if (result.removedNode) removedNode = result.removedNode;
                    return { ...node, children: result.nodes };
                }
                return node;
            });

        return { nodes: newNodes, removedNode };
    };

    const convertTreeToPayload = (nodes, parentDataFlowId = null) => {
        const result = [];
        nodes.forEach((node, index) => {
            result.push({
                organizationNodeId: node.id,
                parentOrganizationNodeId: parentDataFlowId,
                sortOrder: index + 1
            });
            if (node.children?.length) {
                result.push(...convertTreeToPayload(node.children, node.id));
            }
        });
        return result;
    };

    /* ============================= DRAG & DROP (RE-PARENT) ============================= */

    const handleDragStart = node => {
        if (!isEditable || !canEdit) return;
        setDraggedNode(node);
    };

    const handleDrop = targetNode => {
        if (!isEditable || !canEdit || !draggedNode) return;
        if (Number(draggedNode.id) === Number(targetNode.id)) return;

        if (isDescendant(draggedNode, targetNode.id)) {
            Swal.fire('Invalid Move', 'A node cannot be moved inside its own child node.', 'warning');
            setDraggedNode(null);
            return;
        }

        const result = removeNodeFromTree(tree, draggedNode.id);
        if (!result.removedNode) {
            setDraggedNode(null);
            return;
        }

        const movedNode = { ...result.removedNode };

        const addChild = nodes => nodes.map(node => {
            if (Number(node.id) === Number(targetNode.id)) {
                return { ...node, children: [...(node.children || []), movedNode] };
            }
            if (node.children?.length) {
                return { ...node, children: addChild(node.children) };
            }
            return node;
        });

        setTree(addChild(result.nodes));
        setDraggedNode(null);
    };

    /* ============================= REMOVE NODE ============================= */

    const handleRemove = async node => {
        if (!isEditable || !canEdit) return;

        const result = await Swal.fire({
            title: 'Remove node?',
            text: `"${node.name}" will be removed from the Data Flow Hierarchy. Its child nodes will be removed as well.`,
            icon: 'warning',
            showCancelButton: true,
            confirmButtonText: 'Remove',
            cancelButtonText: 'Cancel'
        });

        if (!result.isConfirmed) return;

        const resultTree = removeNodeFromTree(tree, node.id);
        setTree(resultTree.nodes);

        const removedNode = resultTree.removedNode;
        if (removedNode) {
            const exists = availableNodes.some(item => Number(item.id) === Number(removedNode.id));
            if (!exists) {
                setAvailableNodes(prev => [
                    ...prev,
                    { id: removedNode.id, name: removedNode.name, code: removedNode.code, nodeType: removedNode.nodeType }
                ]);
            }
        }
    };

    /* ============================= GRAPHICAL NODE PICKER ============================= */

    const openRootPicker = () => {
        if (!availableNodes.length) {
            Swal.fire('No Nodes Available', 'All organization nodes are already used in the hierarchy.', 'info');
            return;
        }

        setPickerConfig({
            mode: 'root',
            parentNode: null,
            nodes: availableNodes,
            title: 'Add Root Node(s)',
            subtitle: 'Select one or more organization nodes to add as top-level nodes in the hierarchy.'
        });
    };

    const openChildPicker = parentNode => {
        const usedIds = new Set(treeNodes.map(n => Number(n.id)));
        const avail = availableNodes.filter(
            n => !usedIds.has(Number(n.id)) && Number(n.id) !== Number(parentNode.id)
        );

        if (!avail.length) {
            Swal.fire('No Nodes Available', 'All available organization nodes are already used.', 'info');
            return;
        }

        setPickerConfig({
            mode: 'child',
            parentNode,
            nodes: avail,
            title: `Add Child Nodes to "${parentNode.name}"`,
            subtitle: 'Select one or more nodes to add as children of this node.'
        });
    };

    const handlePickerConfirm = chosenNodes => {
        if (!pickerConfig || !chosenNodes.length) return;

        const newNodes = chosenNodes.map(n => ({
            id: n.id,
            name: n.name,
            code: n.code,
            nodeType: n.nodeType,
            children: []
        }));

        if (pickerConfig.mode === 'root') {
            setTree(prev => [...prev, ...newNodes]);
        } else {
            const parentId = pickerConfig.parentNode.id;

            const addChildren = nodes => nodes.map(node => {
                if (Number(node.id) === Number(parentId)) {
                    return { ...node, children: [...(node.children || []), ...newNodes] };
                }
                if (node.children?.length) {
                    return { ...node, children: addChildren(node.children) };
                }
                return node;
            });

            setTree(prev => addChildren(prev));
        }

        const addedIds = new Set(chosenNodes.map(n => Number(n.id)));
        setAvailableNodes(prev => prev.filter(n => !addedIds.has(Number(n.id))));
        setPickerConfig(null);
    };

    /* ============================= SAVE / LOCK / SUBMIT ============================= */

    const handleSave = async () => {
        if (!canSave) return;

        try {
            setSaving(true);

            const payload = { versionNo: version, tree: convertTreeToPayload(tree) };
            const response = await api.post('/organization-data-flow', payload);

            setVersion(response.data?.versionNo || version);
            Swal.fire('Saved', 'Organization Data Flow Hierarchy saved successfully.', 'success');
        } catch (error) {
            console.error('Save Data Flow Error:', error);
            Swal.fire('Save Failed', getApiError(error, 'Unable to save hierarchy.'), 'error');
        } finally {
            setSaving(false);
        }
    };

    const handleLock = async () => {
        if (!isEditable || !canEdit) return;

        const result = await Swal.fire({
            title: 'Lock Hierarchy?',
            text: 'Once locked, direct editing will not be allowed. An edit request will be required to make further changes.',
            icon: 'warning',
            showCancelButton: true,
            confirmButtonText: 'Lock',
            cancelButtonText: 'Cancel'
        });

        if (!result.isConfirmed) return;

        try {
            await api.post('/organization-data-flow/lock');
            setStatus(STATUS.LOCKED);
            Swal.fire('Locked', 'Organization Data Flow Hierarchy has been locked.', 'success');
        } catch (error) {
            console.error('Lock Error:', error);
            Swal.fire('Error', getApiError(error, 'Unable to lock hierarchy.'), 'error');
        }
    };

    const handleSubmitApprovedEdit = async () => {
        if (status !== STATUS.APPROVED_EDIT) return;

        const result = await Swal.fire({
            title: 'Submit Changes?',
            text: 'After submission, this edit permission will be consumed and the hierarchy will be locked again.',
            icon: 'warning',
            showCancelButton: true,
            confirmButtonText: 'Submit',
            cancelButtonText: 'Cancel'
        });

        if (!result.isConfirmed) return;

        try {
            setSaving(true);

            const payload = { versionNo: version, tree: convertTreeToPayload(tree) };
            const response = await api.post('/organization-data-flow/submit-approved-edit', payload);

            setVersion(response.data?.versionNo || version);
            setStatus(STATUS.LOCKED);
            Swal.fire('Submitted', 'Changes submitted successfully. The hierarchy has been locked again.', 'success');
        } catch (error) {
            console.error('Submit Approved Edit Error:', error);
            Swal.fire('Submit Failed', getApiError(error, 'Unable to submit changes.'), 'error');
        } finally {
            setSaving(false);
        }
    };

    /* ============================= EDIT REQUEST ============================= */

    const openRequestModal = async () => {
        try {
            setRequestLoading(true);

            const response = await api.get('/organization-data-flow/request/job-titles');
            setJobTitles(Array.isArray(response.data) ? response.data : []);

            setSelectedJobTitle('');
            setSelectedUser('');
            setRequestReason('');
            setShowRequestModal(true);
        } catch (error) {
            console.error('Load Job Titles Error:', error);
            Swal.fire('Error', getApiError(error, 'Unable to load job titles.'), 'error');
        } finally {
            setRequestLoading(false);
        }
    };

    const handleJobTitleChange = async value => {
        setSelectedJobTitle(value);
        setSelectedUser('');

        if (!value) {
            setUsers([]);
            return;
        }

        try {
            setRequestLoading(true);

            const response = await api.get('/organization-data-flow/request/users', { params: { jobTitleId: value } });
            setUsers(Array.isArray(response.data) ? response.data : []);
        } catch (error) {
            console.error('Load Users Error:', error);
            Swal.fire('Error', getApiError(error, 'Unable to load users.'), 'error');
        } finally {
            setRequestLoading(false);
        }
    };

    const handleSendRequest = async () => {
        if (!selectedJobTitle) {
            Swal.fire('Required', 'Please select a job title.', 'warning');
            return;
        }
        if (!selectedUser) {
            Swal.fire('Required', 'Please select a user.', 'warning');
            return;
        }
        if (!requestReason.trim()) {
            Swal.fire('Required', 'Please enter the reason for the edit request.', 'warning');
            return;
        }

        try {
            setRequestLoading(true);

            await api.post('/organization-data-flow/edit-request', {
                jobTitleId: Number(selectedJobTitle),
                requestedToUserId: Number(selectedUser),
                reason: requestReason.trim()
            });

            setShowRequestModal(false);
            setStatus(STATUS.EDIT_REQUESTED);
            Swal.fire('Request Sent', 'Edit request has been sent successfully.', 'success');
        } catch (error) {
            console.error('Send Edit Request Error:', error);
            Swal.fire('Request Failed', getApiError(error, 'Unable to send edit request.'), 'error');
        } finally {
            setRequestLoading(false);
        }
    };

    /* ============================= APPROVE / REJECT INCOMING REQUESTS ============================= */

    const handleApproveRequest = async request => {
        const result = await Swal.fire({
            title: 'Approve edit request?',
            text: `${request.requestedByName || 'This user'} will get one-time permission to edit the hierarchy.`,
            icon: 'question',
            showCancelButton: true,
            confirmButtonText: 'Approve',
            cancelButtonText: 'Cancel'
        });

        if (!result.isConfirmed) return;

        try {
            setApprovalActionId(request.id);

            await api.post(`/organization-data-flow/edit-request/${request.id}/approve`);

            setPendingApprovals(prev => prev.filter(r => r.id !== request.id));
            Swal.fire('Approved', 'Edit permission granted.', 'success');

            // Refresh the hierarchy in case the current user is also viewing it.
            loadDataFlow();
        } catch (error) {
            console.error('Approve Request Error:', error);
            Swal.fire('Error', getApiError(error, 'Unable to approve this request.'), 'error');
        } finally {
            setApprovalActionId(null);
        }
    };

    const handleRejectRequest = async request => {
        const result = await Swal.fire({
            title: 'Reject edit request?',
            text: `${request.requestedByName || 'This user'}'s request will be rejected.`,
            icon: 'warning',
            showCancelButton: true,
            confirmButtonText: 'Reject',
            cancelButtonText: 'Cancel'
        });

        if (!result.isConfirmed) return;

        try {
            setApprovalActionId(request.id);

            await api.post(`/organization-data-flow/edit-request/${request.id}/reject`);

            setPendingApprovals(prev => prev.filter(r => r.id !== request.id));
            Swal.fire('Rejected', 'Edit request has been rejected.', 'success');
        } catch (error) {
            console.error('Reject Request Error:', error);
            Swal.fire('Error', getApiError(error, 'Unable to reject this request.'), 'error');
        } finally {
            setApprovalActionId(null);
        }
    };

    /* ============================= RENDER ============================= */

    if (loading) {
        return (
            <div className="odfh-loading">
                <div className="spinner-border" role="status" />
                <span>Loading Organization Data Flow Hierarchy...</span>
            </div>
        );
    }

    return (
        <div className="container-fluid odfh-page">

            {/* HEADER */}
            <div className="odfh-header">
                <div>
                    <h4 className="odfh-title">Organization Data Flow Hierarchy</h4>
                    <p className="odfh-subtitle">
                        Configure how organizational node data flows between parent and child nodes.
                    </p>
                </div>
                {canAdd && (
                    <div className="odfh-header-right">
                        <button
                            type="button"
                            className="odfh-approvals-btn"
                            onClick={() => setShowApprovals(true)}
                            title="Edit requests waiting for your approval"
                        >
                            <i className="bi bi-bell" />
                            Approvals
                            {pendingApprovals.length > 0 && (
                                <span className="odfh-approvals-badge">{pendingApprovals.length}</span>
                            )}
                        </button>

                        <div className={`odfh-status ${getStatusClass(status)}`}>
                            {getStatusIcon(status)}
                            {formatStatus(status)}
                        </div>
                        <span className="odfh-version">Version {version}</span>
                    </div>
                )}
            </div>

            {/* ACTION BAR */}
            {canEdit && (
                <>
                    <div className="odfh-action-bar">

                        <div className="odfh-action-left">
                            {isEditable && (
                                <>
                                    {tree.length === 0 && (
                                        <button
                                            type="button"
                                            className="btn btn-outline-primary"
                                            onClick={openRootPicker}
                                            disabled={saving}
                                        >
                                            <i className="bi bi-diagram-2 me-2" />
                                            Add Root Node
                                        </button>
                                    )}
                                    <button type="button" className="btn btn-primary" onClick={handleSave} disabled={saving}>
                                        {saving ? (
                                            <><span className="spinner-border spinner-border-sm me-2" />Saving...</>
                                        ) : (
                                            <><i className="bi bi-save me-2" />Save</>
                                        )}
                                    </button>

                                    {status !== STATUS.APPROVED_EDIT && (
                                        <button type="button" className="btn btn-outline-danger" onClick={handleLock} disabled={saving}>
                                            <i className="bi bi-lock me-2" />
                                            Lock
                                        </button>
                                    )}

                                    {status === STATUS.APPROVED_EDIT && (
                                        <button type="button" className="btn btn-success" onClick={handleSubmitApprovedEdit} disabled={saving}>
                                            <i className="bi bi-check2-circle me-2" />
                                            Submit Changes
                                        </button>
                                    )}

                                </>
                            )}

                            {canRequestEdit && (
                                <button type="button" className="btn btn-warning" onClick={openRequestModal}>
                                    <i className="bi bi-key me-2" />
                                    Request Edit
                                </button>
                            )}
                        </div>


                        <div className="odfh-action-right">
                            {status === STATUS.APPROVED_EDIT && (
                                <span className="odfh-edit-warning">
                                    <i className="bi bi-exclamation-circle me-1" />
                                    One-time edit permission
                                </span>
                            )}
                            {status === STATUS.EDIT_REQUESTED && (
                                <span className="odfh-request-info">
                                    <i className="bi bi-clock-history me-1" />
                                    Edit request pending approval
                                </span>
                            )}
                        </div>

                    </div>
                </>
            )}


            {/* TREE CARD */}
            <div className="odfh-card">
                <div className="odfh-card-header">
                    <div>
                        <h5>Data Flow Structure</h5>
                        <span>{treeNodes.length} node{treeNodes.length !== 1 ? 's' : ''}</span>
                    </div>

                    {canEdit && isEditable && tree.length > 0 && (
                        <div className="odfh-drag-help">
                            <i className="bi bi-arrows-move me-1" />
                            Drag a node onto another to re-parent it, or use the <i className="bi bi-plus-lg mx-1" /> button on a node to add children.
                        </div>
                    )}
                </div>

                <div className="odfh-tree-container">
                    {tree.length === 0 ? (
                        <div className="odfh-empty">
                            <div className="odfh-empty-icon">
                                <i className="bi bi-diagram-3" />
                            </div>
                            <h5>Start building your hierarchy</h5>
                            <p>Select a parent (root) organization node to get started. You can add any number of child nodes to it afterwards.</p>

                            {canEdit && isEditable && (
                                <button type="button" className="btn btn-primary mt-3" onClick={openRootPicker}>
                                    <i className="bi bi-plus-lg me-2" />
                                    Select Parent Node
                                </button>
                            )}

                        </div>
                    ) : (
                        <div className="odfh-org-tree-scroll">
                            <div className="odfh-org-tree">
                                <ul>
                                    {tree.map(node => (
                                        <OrgNode
                                            key={node.id}
                                            node={node}
                                            editable={isEditable}
                                            onDragStart={handleDragStart}
                                            onDrop={handleDrop}
                                            onRemove={handleRemove}
                                            onAddChild={openChildPicker}
                                            draggedNode={draggedNode}
                                            canEdit={canEdit}
                                        />
                                    ))}
                                </ul>
                            </div>
                        </div>
                    )}
                </div>
            </div>

            {/* LOCK MESSAGE */}
            {isLocked && (
                <div className="odfh-locked-banner">
                    <div className="odfh-lock-icon">
                        <i className="bi bi-lock-fill" />
                    </div>
                    <div>
                        <strong>Hierarchy is locked</strong>
                        <p>Direct modifications are disabled. Request edit permission to make changes.</p>
                    </div>
                </div>
            )}

            {/* NODE PICKER MODAL (graphical add for root / child nodes) */}
            {pickerConfig && (
                <NodePickerModal
                    title={pickerConfig.title}
                    subtitle={pickerConfig.subtitle}
                    nodes={pickerConfig.nodes}
                    onConfirm={handlePickerConfirm}
                    onClose={() => setPickerConfig(null)}
                />
            )}

            {/* INCOMING APPROVALS MODAL */}
            {showApprovals && (
                <ApprovalsModal
                    requests={pendingApprovals}
                    actionLoadingId={approvalActionId}
                    onApprove={handleApproveRequest}
                    onReject={handleRejectRequest}
                    onClose={() => setShowApprovals(false)}
                />
            )}

            {/* EDIT REQUEST MODAL */}
            {showRequestModal && (
                <EditRequestModal
                    jobTitles={jobTitles}
                    users={users}
                    selectedJobTitle={selectedJobTitle}
                    selectedUser={selectedUser}
                    reason={requestReason}
                    loading={requestLoading}
                    onJobTitleChange={handleJobTitleChange}
                    onUserChange={setSelectedUser}
                    onReasonChange={setRequestReason}
                    onSubmit={handleSendRequest}
                    onClose={() => setShowRequestModal(false)}
                />
            )}
        </div>
    );
}

/* =============================================================
 * ORG CHART NODE (graphical tree box)
 * ============================================================= */

function OrgNode({ node, editable, onDragStart, onDrop, onRemove, onAddChild, draggedNode, canEdit }) {
    const [expanded, setExpanded] = useState(true);

    const hasChildren = node.children && node.children.length > 0;
    const isDragging = draggedNode && Number(draggedNode.id) === Number(node.id);

    return (
        <li className="odfh-org-li">
            <div
                className={`odfh-org-box ${isDragging ? 'odfh-dragging' : ''}`}
                draggable={editable && canEdit}
                onDragStart={() => onDragStart(node)}
                onDragOver={e => { if (editable && canEdit) e.preventDefault(); }}
                onDrop={e => { e.preventDefault(); if (editable && canEdit) onDrop(node); }}
            >
                <div className="odfh-org-box-top">
                    <span className="odfh-org-icon">
                        <i className={`bi ${getNodeIcon(node.nodeType)}`} />
                    </span>

                    {canEdit && editable && (
                        <span className="odfh-org-drag" title="Drag to re-parent">
                            <i className="bi bi-grip-vertical" />
                        </span>
                    )}
                </div>

                <div className="odfh-org-name">{node.name}</div>

                <div className="odfh-org-meta">
                    {node.code && <span>{node.code}</span>}
                    {node.nodeType && <span className="odfh-org-type">{node.nodeType}</span>}
                </div>

                {canEdit && editable && (
                    <div className="odfh-org-actions">
                        <button type="button" title="Add child node(s)" onClick={() => onAddChild(node)}>
                            <i className="bi bi-plus-lg" />
                        </button>
                        <button type="button" title="Remove node" className="text-danger" onClick={() => onRemove(node)}>
                            <i className="bi bi-trash3" />
                        </button>
                    </div>
                )}

                {hasChildren && (
                    <button
                        type="button"
                        className="odfh-org-toggle"
                        onClick={() => setExpanded(!expanded)}
                        title={expanded ? 'Collapse children' : 'Expand children'}
                    >
                        <i className={`bi ${expanded ? 'bi-dash-circle-fill' : 'bi-plus-circle-fill'}`} />
                    </button>
                )}
            </div>

            {hasChildren && expanded && (
                <ul>
                    {node.children.map(child => (
                        <OrgNode
                            key={child.id}
                            node={child}
                            editable={editable}
                            onDragStart={onDragStart}
                            onDrop={onDrop}
                            onRemove={onRemove}
                            onAddChild={onAddChild}
                            draggedNode={draggedNode}
                            canEdit={canEdit}
                        />
                    ))}
                </ul>
            )}
        </li>
    );
}

/* =============================================================
 * NODE PICKER MODAL (graphical multi-select add)
 * ============================================================= */

function NodeGrid({ nodes, selectedIds, onToggle, emptyMessage }) {
    if (!nodes.length) {
        return (
            <div className="odfh-picker-empty">
                <i className="bi bi-inboxes" />
                <p>{emptyMessage}</p>
            </div>
        );
    }

    return (
        <div className="odfh-node-grid">
            {nodes.map(node => {
                const selected = selectedIds.has(Number(node.id));
                return (
                    <button
                        type="button"
                        key={node.id}
                        className={`odfh-node-card ${selected ? 'selected' : ''}`}
                        onClick={() => onToggle(node.id)}
                    >
                        <span className="odfh-node-card-check">
                            <i className={`bi ${selected ? 'bi-check-circle-fill' : 'bi-circle'}`} />
                        </span>
                        <span className="odfh-node-card-icon">
                            <i className={`bi ${getNodeIcon(node.nodeType)}`} />
                        </span>
                        <span className="odfh-node-card-name">{node.name}</span>
                        {node.code && <span className="odfh-node-card-code">{node.code}</span>}
                        {node.nodeType && <span className="odfh-node-card-type">{node.nodeType}</span>}
                    </button>
                );
            })}
        </div>
    );
}

function NodePickerModal({ title, subtitle, nodes, onConfirm, onClose }) {
    const [search, setSearch] = useState('');
    const [selected, setSelected] = useState(new Set());

    const filtered = useMemo(() => {
        const term = search.trim().toLowerCase();
        if (!term) return nodes;
        return nodes.filter(n =>
            n.name?.toLowerCase().includes(term) ||
            (n.code || '').toLowerCase().includes(term) ||
            (n.nodeType || '').toLowerCase().includes(term)
        );
    }, [nodes, search]);

    const toggle = id => {
        setSelected(prev => {
            const next = new Set(prev);
            const key = Number(id);
            if (next.has(key)) next.delete(key); else next.add(key);
            return next;
        });
    };

    const handleConfirm = () => {
        const chosen = nodes.filter(n => selected.has(Number(n.id)));
        if (!chosen.length) return;
        onConfirm(chosen);
    };

    return (
        <div className="odfh-modal-backdrop" onMouseDown={e => { if (e.target === e.currentTarget) onClose(); }}>
            <div className="odfh-modal odfh-picker-modal">
                <div className="odfh-modal-header">
                    <div>
                        <h5>{title}</h5>
                        {subtitle && <span>{subtitle}</span>}
                    </div>
                    <button type="button" className="btn-close" onClick={onClose} />
                </div>

                <div className="odfh-modal-body">
                    <div className="odfh-picker-search">
                        <i className="bi bi-search" />
                        <input
                            type="text"
                            placeholder="Search nodes by name, code or type..."
                            value={search}
                            onChange={e => setSearch(e.target.value)}
                            autoFocus
                        />
                    </div>

                    <NodeGrid
                        nodes={filtered}
                        selectedIds={selected}
                        onToggle={toggle}
                        emptyMessage="No matching nodes found."
                    />
                </div>

                <div className="odfh-modal-footer odfh-picker-footer">
                    <span className="odfh-picker-count">{selected.size} selected</span>
                    <div className="odfh-picker-actions">
                        <button type="button" className="btn btn-light" onClick={onClose}>Cancel</button>
                        <button type="button" className="btn btn-primary" disabled={!selected.size} onClick={handleConfirm}>
                            <i className="bi bi-plus-lg me-2" />
                            Add{selected.size > 0 ? ` (${selected.size})` : ''}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
}

/* =============================================================
 * INCOMING APPROVALS MODAL
 * ============================================================= */

function ApprovalsModal({ requests, actionLoadingId, onApprove, onReject, onClose }) {
    return (
        <div className="odfh-modal-backdrop" onMouseDown={e => { if (e.target === e.currentTarget) onClose(); }}>
            <div className="odfh-modal odfh-approvals-modal">
                <div className="odfh-modal-header">
                    <div>
                        <h5>Edit Requests Awaiting Your Approval</h5>
                        <span>Review and approve or reject requests sent to you.</span>
                    </div>
                    <button type="button" className="btn-close" onClick={onClose} />
                </div>

                <div className="odfh-modal-body">
                    {requests.length === 0 ? (
                        <div className="odfh-picker-empty">
                            <i className="bi bi-check2-circle" />
                            <p>No pending requests right now.</p>
                        </div>
                    ) : (
                        <div className="odfh-approvals-list">
                            {requests.map(request => {
                                const busy = actionLoadingId === request.id;
                                return (
                                    <div key={request.id} className="odfh-approval-card">
                                        <div className="odfh-approval-top">
                                            <div className="odfh-approval-avatar">
                                                <i className="bi bi-person" />
                                            </div>
                                            <div>
                                                <div className="odfh-approval-name">{request.requestedByName || 'Unknown user'}</div>
                                                {request.jobTitleName && (
                                                    <div className="odfh-approval-meta">Requested as {request.jobTitleName}</div>
                                                )}
                                            </div>
                                        </div>

                                        {request.reason && (
                                            <div className="odfh-approval-reason">{request.reason}</div>
                                        )}

                                        <div className="odfh-approval-actions">
                                            <button
                                                type="button"
                                                className="btn btn-sm btn-outline-danger"
                                                disabled={busy}
                                                onClick={() => onReject(request)}
                                            >
                                                <i className="bi bi-x-lg me-1" />
                                                Reject
                                            </button>
                                            <button
                                                type="button"
                                                className="btn btn-sm btn-primary"
                                                disabled={busy}
                                                onClick={() => onApprove(request)}
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
                                );
                            })}
                        </div>
                    )}
                </div>

                <div className="odfh-modal-footer">
                    <button type="button" className="btn btn-light" onClick={onClose}>Close</button>
                </div>
            </div>
        </div>
    );
}

/* =============================================================
 * EDIT REQUEST MODAL
 * ============================================================= */

function EditRequestModal({
    jobTitles, users, selectedJobTitle, selectedUser, reason, loading,
    onJobTitleChange, onUserChange, onReasonChange, onSubmit, onClose
}) {
    return (
        <div className="odfh-modal-backdrop" onMouseDown={e => { if (e.target === e.currentTarget && !loading) onClose(); }}>
            <div className="odfh-modal">
                <div className="odfh-modal-header">
                    <div>
                        <h5>Request Edit Permission</h5>
                        <span>Select the job title and user who should approve the request.</span>
                    </div>
                    <button type="button" className="btn-close" onClick={onClose} disabled={loading} />
                </div>

                <div className="odfh-modal-body">
                    <div className="mb-3">
                        <label className="form-label">Request To Job Title<span className="text-danger">*</span></label>
                        <select className="form-select" value={selectedJobTitle} onChange={e => onJobTitleChange(e.target.value)} disabled={loading}>
                            <option value="">Select Job Title</option>
                            {jobTitles.map(job => <option key={job.id} value={job.id}>{job.name}</option>)}
                        </select>
                    </div>

                    <div className="mb-3">
                        <label className="form-label">Request To User<span className="text-danger">*</span></label>
                        <select className="form-select" value={selectedUser} onChange={e => onUserChange(e.target.value)} disabled={loading || !selectedJobTitle}>
                            <option value="">Select User</option>
                            {users.map(user => (
                                <option key={user.id} value={user.id}>
                                    {user.name}{user.username ? ` (${user.username})` : ''}
                                </option>
                            ))}
                        </select>
                    </div>

                    <div className="mb-3">
                        <label className="form-label">Reason<span className="text-danger">*</span></label>
                        <textarea
                            className="form-control"
                            rows="4"
                            value={reason}
                            onChange={e => onReasonChange(e.target.value)}
                            placeholder="Enter the reason for requesting edit permission..."
                            disabled={loading}
                        />
                    </div>
                </div>

                <div className="odfh-modal-footer">
                    <button type="button" className="btn btn-light" onClick={onClose} disabled={loading}>Cancel</button>
                    <button type="button" className="btn btn-primary" onClick={onSubmit} disabled={loading}>
                        {loading ? (
                            <><span className="spinner-border spinner-border-sm me-2" />Sending...</>
                        ) : (
                            <><i className="bi bi-send me-2" />Send Request</>
                        )}
                    </button>
                </div>
            </div>
        </div>
    );
}

/* =============================================================
 * HELPERS
 * ============================================================= */

function formatStatus(status) {
    switch (status) {
        case STATUS.DRAFT: return 'Draft';
        case STATUS.UNLOCKED: return 'Unlocked';
        case STATUS.LOCKED: return 'Locked';
        case STATUS.EDIT_REQUESTED: return 'Edit Request Pending';
        case STATUS.APPROVED_EDIT: return 'Edit Permission Granted';
        default: return status;
    }
}

function getStatusClass(status) {
    switch (status) {
        case STATUS.LOCKED: return 'status-locked';
        case STATUS.EDIT_REQUESTED: return 'status-pending';
        case STATUS.APPROVED_EDIT: return 'status-approved';
        case STATUS.UNLOCKED: return 'status-unlocked';
        default: return 'status-draft';
    }
}

function getStatusIcon(status) {
    switch (status) {
        case STATUS.LOCKED: return <i className="bi bi-lock-fill" />;
        case STATUS.EDIT_REQUESTED: return <i className="bi bi-clock-history" />;
        case STATUS.APPROVED_EDIT: return <i className="bi bi-unlock-fill" />;
        default: return <i className="bi bi-pencil-square" />;
    }
}