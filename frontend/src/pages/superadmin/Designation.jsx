import { useEffect, useState } from 'react';
import {
    Pencil,
    Plus,
    Power,
    X
} from 'lucide-react';

import api from '../../api/axios';
import usePageAccess from '../../hooks/usePageAccess';

const emptyForm = {
    name: '',
    desShort: '',
    sequence: 0
};

export default function Designation() {

    // =====================================================
    // PAGE ACCESS
    // Automatically uses current URL
    // =====================================================

    const {canView, canAdd, canEdit, canDelete} = usePageAccess();

    const [designations, setDesignations] = useState([]);

    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);

    const [modal, setModal] = useState(null);
    const [activeDesignation, setActiveDesignation] =
        useState(null);

    const [form, setForm] = useState(emptyForm);
    const [error, setError] = useState('');

    // =====================================================
    // LOAD DESIGNATIONS
    // =====================================================

    async function loadDesignations() {

        setLoading(true);
        setError('');

        try {

            const res =
                await api.get('/designations');

            setDesignations(res.data || []);

        } catch (err) {

            setError(
                err.response?.data?.message ||
                'Unable to load designations.'
            );

        } finally {

            setLoading(false);

        }
    }

    useEffect(() => {

        if (canView) {
            loadDesignations();
        } else {
            setLoading(false);
        }

    }, [canView]);

    // =====================================================
    // MODAL
    // =====================================================

    function closeModal() {

        setModal(null);
        setActiveDesignation(null);
        setForm(emptyForm);
        setError('');

    }

    function openCreate() {

        setForm(emptyForm);
        setActiveDesignation(null);
        setError('');
        setModal('create');

    }

    function openEdit(designation) {

        setActiveDesignation(designation);

        setForm({
            name: designation.name || '',
            desShort: designation.des_short || '',
            sequence: designation.sequence ?? 0
        });

        setError('');
        setModal('edit');

    }

    // =====================================================
    // FORM VALIDATION
    // =====================================================

    function validateForm() {

        const name =
            form.name.trim();

        const desShort =
            form.desShort.trim();

        if (!name) {
            return 'Designation name is required.';
        }

        if (name.length > 100) {
            return 'Designation name cannot exceed 100 characters.';
        }

        if (!desShort) {
            return 'Designation short name is required.';
        }

        if (desShort.length > 50) {
            return 'Designation short name cannot exceed 50 characters.';
        }

        if (!/^[A-Za-z0-9 _-]+$/.test(desShort)) {
            return (
                'Designation short name can contain only ' +
                'letters, numbers, spaces, hyphens and underscores.'
            );
        }

        if (Number(form.sequence) < 0) {
            return 'Sequence cannot be negative.';
        }

        return null;
    }

    // =====================================================
    // CREATE / UPDATE
    // =====================================================

    async function handleSubmit(e) {

        e.preventDefault();

        setError('');

        const validationError =
            validateForm();

        if (validationError) {

            setError(validationError);
            return;

        }

        const isEdit =
            modal === 'edit' &&
            activeDesignation;

        const actionText =
            isEdit
                ? 'update'
                : 'create';

        const confirmed =
            window.confirm(
                isEdit
                    ? `Are you sure you want to update "${form.name.trim()}"?`
                    : `Are you sure you want to create "${form.name.trim()}"?`
            );

        if (!confirmed) {
            return;
        }

        setSaving(true);

        try {

            const payload = {
                name: form.name.trim(),
                desShort: form.desShort.trim(),
                sequence:
                    Number(form.sequence) || 0
            };

            if (isEdit) {

                await api.put(
                    `/designations/${activeDesignation.id}`,
                    payload
                );

            } else {

                await api.post(
                    '/designations',
                    payload
                );

            }

            closeModal();

            await loadDesignations();

        } catch (err) {

            setError(
                err.response?.data?.message ||
                `Unable to ${actionText} designation.`
            );

        } finally {

            setSaving(false);

        }
    }

    // =====================================================
    // DEACTIVATE
    // DELETE PERMISSION
    // =====================================================

    async function handleDeactivate(
        designation
    ) {

        if (!designation?.id) {
            return;
        }

        const confirmed =
            window.confirm(
                `Are you sure you want to deactivate "${designation.name}"?\n\nThe designation will no longer be active.`
            );

        if (!confirmed) {
            return;
        }

        try {

            await api.delete(
                `/designations/${designation.id}`
            );

            await loadDesignations();

        } catch (err) {

            setError(
                err.response?.data?.message ||
                'Unable to deactivate designation.'
            );

        }
    }

    // =====================================================
    // ACTIVATE
    // =====================================================

    async function handleActivate(
        designation
    ) {

        if (!designation?.id) {
            return;
        }

        const confirmed =
            window.confirm(
                `Are you sure you want to activate "${designation.name}"?`
            );

        if (!confirmed) {
            return;
        }

        try {

            await api.put(
                `/designations/${designation.id}/activate`
            );

            await loadDesignations();

        } catch (err) {

            setError(
                err.response?.data?.message ||
                'Unable to activate designation.'
            );

        }
    }

    // =====================================================
    // VIEW
    // =====================================================

    if (!canView) {

        return null;

    }

    // =====================================================
    // UI
    // =====================================================

    return (

        <div className="page">

            {/* =================================================
                HEADER
            ================================================= */}

            <div className="page-toolbar">

                <div>

                    <h1>
                        Designations
                    </h1>

                    <p className="muted">
                        Create and manage employee designations.
                    </p>

                </div>

                {canAdd && (

                    <button
                        type="button"
                        className="btn btn-primary"
                        onClick={openCreate}
                    >
                        <Plus size={16} />

                        Add Designation
                    </button>

                )}

            </div>

            {/* =================================================
                ERROR
            ================================================= */}

            {error && !modal && (

                <div className="error-text mb-16">
                    {error}
                </div>

            )}

            {/* =================================================
                TABLE
            ================================================= */}

            <div className="card">

                {loading ? (

                    <p className="muted">
                        Loading designations...
                    </p>

                ) : designations.length === 0 ? (

                    <div className="empty-state">

                        <p>
                            No designations found.
                        </p>

                        {canAdd && (

                            <button
                                className="btn btn-primary"
                                onClick={openCreate}
                            >
                                <Plus size={16} />
                                Add Designation
                            </button>

                        )}

                    </div>

                ) : (

                    <div
                        style={{
                            overflowX: 'auto'
                        }}
                    >

                        <table className="simple-table">

                            <thead>

                                <tr>

                                    <th>
                                        #
                                    </th>

                                    <th>
                                        Designation
                                    </th>

                                    <th>
                                        Short Name
                                    </th>

                                    <th>
                                        Sequence
                                    </th>

                                    <th>
                                        Status
                                    </th>

                                    {(canEdit ||
                                        canDelete) && (

                                        <th>
                                            Actions
                                        </th>

                                    )}

                                </tr>

                            </thead>

                            <tbody>

                                {designations.map(
                                    (designation, index) => (

                                        <tr
                                            key={
                                                designation.id
                                            }
                                        >

                                            <td>
                                                {index + 1}
                                            </td>

                                            <td>
                                                <strong>
                                                    {
                                                        designation.name
                                                    }
                                                </strong>
                                            </td>

                                            <td>
                                                {
                                                    designation.des_short
                                                }
                                            </td>

                                            <td>
                                                {
                                                    designation.sequence
                                                }
                                            </td>

                                            <td>

                                                <span
                                                    className={
                                                        Number(
                                                            designation.active
                                                        ) === 1
                                                            ? 'badge badge-active'
                                                            : 'badge badge-inactive'
                                                    }
                                                >
                                                    {
                                                        Number(
                                                            designation.active
                                                        ) === 1
                                                            ? 'ACTIVE'
                                                            : 'INACTIVE'
                                                    }
                                                </span>

                                            </td>

                                            {(canEdit ||
                                                canDelete) && (

                                                <td>

                                                    <div
                                                        style={{
                                                            display: 'flex',
                                                            gap: 6
                                                        }}
                                                    >

                                                        {/* EDIT */}

                                                        {canEdit && (

                                                            <button
                                                                type="button"
                                                                className="icon-btn"
                                                                title="Edit"
                                                                onClick={() =>
                                                                    openEdit(
                                                                        designation
                                                                    )
                                                                }
                                                            >
                                                                <Pencil
                                                                    size={15}
                                                                />
                                                            </button>

                                                        )}

                                                        {/* DELETE = DEACTIVATE */}

                                                        {canDelete &&
                                                            Number(
                                                                designation.active
                                                            ) === 1 && (

                                                                <button
                                                                    type="button"
                                                                    className="icon-btn icon-btn-danger"
                                                                    title="Deactivate"
                                                                    onClick={() =>
                                                                        handleDeactivate(
                                                                            designation
                                                                        )
                                                                    }
                                                                >
                                                                    <Power
                                                                        size={15}
                                                                    />
                                                                </button>

                                                            )}

                                                        {/* ACTIVATE */}

                                                        {canEdit &&
                                                            Number(
                                                                designation.active
                                                            ) === 0 && (

                                                                <button
                                                                    type="button"
                                                                    className="icon-btn"
                                                                    title="Activate"
                                                                    onClick={() =>
                                                                        handleActivate(
                                                                            designation
                                                                        )
                                                                    }
                                                                >
                                                                    <Power
                                                                        size={15}
                                                                    />
                                                                </button>

                                                            )}

                                                    </div>

                                                </td>

                                            )}

                                        </tr>

                                    )
                                )}

                            </tbody>

                        </table>

                    </div>

                )}

            </div>

            {/* =================================================
                CREATE / EDIT MODAL
            ================================================= */}

            {modal && (

                <div
                    className="app-modal-overlay"
                    onClick={closeModal}
                >

                    <div
                        className="app-modal"
                        onClick={(e) =>
                            e.stopPropagation()
                        }
                    >

                        <div className="app-modal-header">

                            <h3>

                                {modal === 'edit'
                                    ? 'Edit Designation'
                                    : 'Add Designation'}

                            </h3>

                            <button
                                type="button"
                                className="app-modal-close"
                                onClick={closeModal}
                            >
                                <X size={18} />
                            </button>

                        </div>

                        <form
                            onSubmit={handleSubmit}
                        >

                            <div className="app-modal-body">

                                {/* NAME */}

                                <label>
                                    Designation Name *
                                </label>

                                <input
                                    type="text"
                                    maxLength={100}
                                    value={form.name}
                                    onChange={(e) =>
                                        setForm(
                                            (current) => ({
                                                ...current,
                                                name:
                                                    e.target.value
                                            })
                                        )
                                    }
                                    placeholder="Enter designation name"
                                    required
                                />

                                {/* SHORT NAME */}

                                <label>
                                    Short Name *
                                </label>

                                <input
                                    type="text"
                                    maxLength={50}
                                    value={form.desShort}
                                    onChange={(e) =>
                                        setForm(
                                            (current) => ({
                                                ...current,
                                                desShort:
                                                    e.target.value
                                            })
                                        )
                                    }
                                    placeholder="Enter short name"
                                    required
                                />

                                {/* SEQUENCE */}

                                <label>
                                    Sequence *
                                </label>

                                <input
                                    type="number"
                                    min="0"
                                    value={form.sequence}
                                    onChange={(e) =>
                                        setForm(
                                            (current) => ({
                                                ...current,
                                                sequence:
                                                    e.target.value
                                            })
                                        )
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