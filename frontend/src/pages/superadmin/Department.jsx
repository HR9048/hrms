import { useEffect, useState } from 'react';
import { Pencil, Plus, Power, X } from 'lucide-react';

import api from '../../api/axios';
import usePageAccess from '../../hooks/usePageAccess';

const emptyForm = {
    depName: '',
    depShort: '',
    sequence: 0
};

export default function Department() {
    const {
        canView,
        canAdd,
        canEdit,
        canDelete
    } = usePageAccess();

    const [departments, setDepartments] = useState([]);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);

    const [modal, setModal] = useState(null);
    const [activeDepartment, setActiveDepartment] = useState(null);
    const [form, setForm] = useState(emptyForm);
    const [error, setError] = useState('');

    // ---------------------------------------------------------
    // Load
    // ---------------------------------------------------------

    async function loadDepartments() {
        setLoading(true);
        setError('');

        try {
            const res = await api.get('/departments');
            setDepartments(res.data || []);
        } catch (err) {
            setError(
                err.response?.data?.message ||
                'Unable to load departments.'
            );
        } finally {
            setLoading(false);
        }
    }

    useEffect(() => {
        if (canView) {
            loadDepartments();
        } else {
            setLoading(false);
        }
    }, [canView]);

    // ---------------------------------------------------------
    // Modal
    // ---------------------------------------------------------

    function closeModal() {
        setModal(null);
        setActiveDepartment(null);
        setForm(emptyForm);
        setError('');
    }

    function openCreate() {
        setForm(emptyForm);
        setActiveDepartment(null);
        setError('');
        setModal('create');
    }

    function openEdit(department) {
        setActiveDepartment(department);

        setForm({
            depName: department.dep_name || '',
            depShort: department.dep_short || '',
            sequence: department.sequence ?? 0
        });

        setError('');
        setModal('edit');
    }

    // ---------------------------------------------------------
    // Validation
    // ---------------------------------------------------------

    function validateForm() {
        const depName = form.depName.trim();
        const depShort = form.depShort.trim();

        if (!depName) {
            return 'Department name is required.';
        }

        if (depName.length > 100) {
            return 'Department name cannot exceed 100 characters.';
        }

        if (!depShort) {
            return 'Department short name is required.';
        }

        if (depShort.length > 50) {
            return 'Department short name cannot exceed 50 characters.';
        }

        if (!/^[A-Za-z0-9 _-]+$/.test(depShort)) {
            return 'Department short name can contain only letters, numbers, spaces, hyphens and underscores.';
        }

        if (Number(form.sequence) < 0) {
            return 'Sequence cannot be negative.';
        }

        return null;
    }

    // ---------------------------------------------------------
    // Add / Edit
    // ---------------------------------------------------------

    async function handleSubmit(e) {
        e.preventDefault();
        setError('');

        const validationError = validateForm();

        if (validationError) {
            setError(validationError);
            return;
        }

        const depName = form.depName.trim();

        const isEdit =
            modal === 'edit' && activeDepartment;

        const confirmed = window.confirm(
            isEdit
                ? `Are you sure you want to update "${depName}"?`
                : `Are you sure you want to create "${depName}"?`
        );

        if (!confirmed) {
            return;
        }

        setSaving(true);

        const payload = {
            depName,
            depShort: form.depShort.trim(),
            sequence: Number(form.sequence) || 0
        };

        try {
            if (isEdit) {
                await api.put(
                    `/departments/${activeDepartment.id}`,
                    payload
                );
            } else {
                await api.post('/departments', payload);
            }

            closeModal();
            await loadDepartments();
        } catch (err) {
            setError(
                err.response?.data?.message ||
                'Something went wrong while saving the department.'
            );
        } finally {
            setSaving(false);
        }
    }

    // ---------------------------------------------------------
    // Deactivate
    // ---------------------------------------------------------

    async function handleDeactivate(department) {
        if (!department?.id) {
            return;
        }

        const confirmed = window.confirm(
            `Are you sure you want to deactivate "${department.dep_name}"?`
        );

        if (!confirmed) {
            return;
        }

        try {
            await api.delete(
                `/departments/${department.id}`
            );

            await loadDepartments();
        } catch (err) {
            setError(
                err.response?.data?.message ||
                'Unable to deactivate the department.'
            );
        }
    }

    // ---------------------------------------------------------
    // Activate
    // ---------------------------------------------------------

    async function handleActivate(department) {
        if (!department?.id) {
            return;
        }

        const confirmed = window.confirm(
            `Are you sure you want to activate "${department.dep_name}"?`
        );

        if (!confirmed) {
            return;
        }

        try {
            await api.put(
                `/departments/${department.id}/activate`
            );

            await loadDepartments();
        } catch (err) {
            setError(
                err.response?.data?.message ||
                'Unable to activate the department.'
            );
        }
    }

    // ---------------------------------------------------------
    // View permission
    // ---------------------------------------------------------

    if (!canView) {
        return null;
    }

    return (
        <div className="page">

            {/* HEADER */}

            <div className="page-toolbar">
                <div>
                    <h1>Departments</h1>

                    <p className="muted">
                        Create and manage organization departments.
                    </p>
                </div>

                {canAdd && (
                    <button
                        type="button"
                        className="btn btn-primary"
                        onClick={openCreate}
                    >
                        <Plus
                            size={16}
                            style={{
                                marginRight: 4,
                                verticalAlign: -3
                            }}
                        />

                        Add Department
                    </button>
                )}
            </div>

            {/* ERROR */}

            {error && !modal && (
                <div className="error-text mb-16">
                    {error}
                </div>
            )}

            {/* TABLE */}

            <div className="card">

                {loading ? (
                    <p className="muted">
                        Loading departments...
                    </p>
                ) : departments.length === 0 ? (

                    <div className="empty-state">
                        <p>
                            No departments found.
                        </p>

                        {canAdd && (
                            <button
                                className="btn btn-primary"
                                onClick={openCreate}
                            >
                                <Plus size={16} />
                                Add Department
                            </button>
                        )}
                    </div>

                ) : (

                    <div style={{ overflowX: 'auto' }}>
                        <table className="simple-table">

                            <thead>
                                <tr>
                                    <th>#</th>
                                    <th>Department Name</th>
                                    <th>Short Name</th>
                                    <th>Sequence</th>
                                    <th>Status</th>

                                    {(canEdit || canDelete) && (
                                        <th>Actions</th>
                                    )}
                                </tr>
                            </thead>

                            <tbody>
                                {departments.map((department, index) => (
                                    <tr key={department.id}>

                                        <td>
                                            {index + 1}
                                        </td>

                                        <td>
                                            <strong>
                                                {department.dep_name}
                                            </strong>
                                        </td>

                                        <td>
                                            {department.dep_short}
                                        </td>

                                        <td>
                                            {department.sequence}
                                        </td>

                                        <td>
                                            <span
                                                className={`badge ${
                                                    Number(department.active) === 1
                                                        ? 'badge-active'
                                                        : 'badge-inactive'
                                                }`}
                                            >
                                                {Number(department.active) === 1
                                                    ? 'ACTIVE'
                                                    : 'INACTIVE'}
                                            </span>
                                        </td>

                                        {(canEdit || canDelete) && (
                                            <td>
                                                <div
                                                    style={{
                                                        display: 'flex',
                                                        gap: 6
                                                    }}
                                                >

                                                    {canEdit && (
                                                        <button
                                                            type="button"
                                                            className="icon-btn"
                                                            title="Edit"
                                                            onClick={() =>
                                                                openEdit(department)
                                                            }
                                                        >
                                                            <Pencil size={15} />
                                                        </button>
                                                    )}

                                                    {canDelete &&
                                                        Number(department.active) === 1 && (
                                                            <button
                                                                type="button"
                                                                className="icon-btn icon-btn-danger"
                                                                title="Deactivate"
                                                                onClick={() =>
                                                                    handleDeactivate(
                                                                        department
                                                                    )
                                                                }
                                                            >
                                                                <Power size={15} />
                                                            </button>
                                                        )}

                                                    {canEdit &&
                                                        Number(department.active) === 0 && (
                                                            <button
                                                                type="button"
                                                                className="icon-btn"
                                                                title="Activate"
                                                                onClick={() =>
                                                                    handleActivate(
                                                                        department
                                                                    )
                                                                }
                                                            >
                                                                <Power size={15} />
                                                            </button>
                                                        )}

                                                </div>
                                            </td>
                                        )}

                                    </tr>
                                ))}
                            </tbody>

                        </table>
                    </div>
                )}

            </div>

            {/* CREATE / EDIT MODAL */}

            {modal && (
                <div
                    className="app-modal-overlay"
                    onClick={closeModal}
                >
                    <div
                        className="app-modal"
                        onClick={(e) => e.stopPropagation()}
                    >

                        <div className="app-modal-header">

                            <h3>
                                {modal === 'edit'
                                    ? 'Edit Department'
                                    : 'Add Department'}
                            </h3>

                            <button
                                type="button"
                                className="app-modal-close"
                                onClick={closeModal}
                            >
                                <X size={18} />
                            </button>

                        </div>

                        <form onSubmit={handleSubmit}>

                            <div className="app-modal-body">

                                {/* Department Name */}

                                <label>
                                    Department Name *
                                </label>

                                <input
                                    type="text"
                                    maxLength={100}
                                    value={form.depName}
                                    onChange={(e) =>
                                        setForm((current) => ({
                                            ...current,
                                            depName: e.target.value
                                        }))
                                    }
                                    placeholder="Enter department name"
                                    required
                                />

                                {/* Short Name */}

                                <label>
                                    Short Name *
                                </label>

                                <input
                                    type="text"
                                    maxLength={50}
                                    value={form.depShort}
                                    onChange={(e) =>
                                        setForm((current) => ({
                                            ...current,
                                            depShort: e.target.value
                                        }))
                                    }
                                    placeholder="e.g. HR"
                                    required
                                />

                                {/* Sequence */}

                                <label>
                                    Sequence *
                                </label>

                                <input
                                    type="number"
                                    min="0"
                                    value={form.sequence}
                                    onChange={(e) =>
                                        setForm((current) => ({
                                            ...current,
                                            sequence: e.target.value
                                        }))
                                    }
                                    required
                                />

                                {error && (
                                    <p className="error-text">
                                        {error}
                                    </p>
                                )}

                            </div>

                            <div className="app-modal-footer">

                                <button
                                    type="button"
                                    className="btn btn-ghost"
                                    onClick={closeModal}
                                >
                                    Cancel
                                </button>

                                <button
                                    type="submit"
                                    className="btn btn-primary"
                                    disabled={saving}
                                >
                                    {saving
                                        ? 'Saving...'
                                        : modal === 'edit'
                                            ? 'Update'
                                            : 'Save'}
                                </button>

                            </div>

                        </form>

                    </div>
                </div>
            )}

        </div>
    );
}
