import { useEffect, useState } from 'react';

import {
    Pencil,
    Plus,
    Power,
    X
} from 'lucide-react';

import api from '../../api/axios';
import { useAuth } from '../../context/AuthContext';


const emptyForm = {
    roleCode: '',
    roleName: '',
    description: '',
    sequence: 0
};


export default function Roles() {

    const { account } = useAuth();

    // =====================================================
    // SUPERADMIN ACCESS
    // =====================================================

    const isSuperAdmin =
        String(account?.roleCode || '').toUpperCase() ===
        'SUPERADMIN';


    const [roles, setRoles] = useState([]);

    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);

    const [modal, setModal] = useState(null);
    const [activeRole, setActiveRole] = useState(null);

    const [form, setForm] = useState(emptyForm);

    const [error, setError] = useState('');


    // =====================================================
    // LOAD ROLES
    // =====================================================

    async function loadRoles() {

        setLoading(true);
        setError('');

        try {

            const res =
                await api.get('/roles');

            setRoles(res.data || []);

        } catch (err) {

            console.error(err);

            setError(
                err.response?.data?.message ||
                'Unable to load roles.'
            );

        } finally {

            setLoading(false);

        }
    }


    // =====================================================
    // INITIAL LOAD
    // =====================================================

    useEffect(() => {

        if (isSuperAdmin) {
            loadRoles();
        } else {
            setLoading(false);
        }

    }, [isSuperAdmin]);


    // =====================================================
    // MODAL
    // =====================================================

    function closeModal() {

        setModal(null);
        setActiveRole(null);
        setForm(emptyForm);
        setError('');

    }


    function openCreate() {

        setForm(emptyForm);
        setActiveRole(null);
        setError('');
        setModal('create');

    }


    function openEdit(role) {

        setActiveRole(role);

        setForm({
            roleCode: role.role_code || '',
            roleName: role.role_name || '',
            description: role.description || '',
            sequence: role.sequence ?? 0
        });

        setError('');
        setModal('edit');

    }


    // =====================================================
    // FORM VALIDATION
    // =====================================================

    function validateForm() {

        const roleCode =
            form.roleCode.trim();

        const roleName =
            form.roleName.trim();

        const description =
            form.description.trim();


        if (!roleCode) {
            return 'Role code is required.';
        }


        if (roleCode.length > 50) {
            return 'Role code cannot exceed 50 characters.';
        }


        if (!/^[A-Za-z0-9_-]+$/.test(roleCode)) {
            return (
                'Role code can contain only ' +
                'letters, numbers, hyphens and underscores.'
            );
        }


        if (!roleName) {
            return 'Role name is required.';
        }


        if (roleName.length > 100) {
            return 'Role name cannot exceed 100 characters.';
        }


        if (description.length > 255) {
            return 'Description cannot exceed 255 characters.';
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
            activeRole;


        const actionText =
            isEdit
                ? 'update'
                : 'create';


        const confirmed =
            window.confirm(

                isEdit
                    ? `Are you sure you want to update "${form.roleName.trim()}"?`
                    : `Are you sure you want to create "${form.roleName.trim()}"?`

            );


        if (!confirmed) {
            return;
        }


        setSaving(true);


        try {

            const payload = {

                roleCode:
                    form.roleCode.trim(),

                roleName:
                    form.roleName.trim(),

                description:
                    form.description.trim() || null,

                sequence:
                    Number(form.sequence) || 0

            };


            if (isEdit) {

                await api.put(
                    `/roles/${activeRole.id}`,
                    payload
                );

            } else {

                await api.post(
                    '/roles',
                    payload
                );

            }


            closeModal();

            await loadRoles();


        } catch (err) {

            console.error(err);

            setError(
                err.response?.data?.message ||
                `Unable to ${actionText} role.`
            );

        } finally {

            setSaving(false);

        }
    }


    // =====================================================
    // DEACTIVATE
    // DELETE = DEACTIVATE
    // =====================================================

    async function handleDeactivate(role) {

        if (!role?.id) {
            return;
        }


        // SUPERADMIN ROLE CANNOT BE DEACTIVATED

        if (
            String(role.role_code || '').toUpperCase() ===
            'SUPERADMIN'
        ) {

            setError(
                'SUPERADMIN role cannot be deactivated.'
            );

            return;
        }


        const confirmed =
            window.confirm(

                `Are you sure you want to deactivate "${role.role_name}"?\n\nThe role will no longer be active.`

            );


        if (!confirmed) {
            return;
        }


        try {

            setError('');


            await api.delete(
                `/roles/${role.id}`
            );


            await loadRoles();


        } catch (err) {

            console.error(err);

            setError(
                err.response?.data?.message ||
                'Unable to deactivate role.'
            );

        }
    }


    // =====================================================
    // ACTIVATE
    // =====================================================

    async function handleActivate(role) {

        if (!role?.id) {
            return;
        }


        const confirmed =
            window.confirm(
                `Are you sure you want to activate "${role.role_name}"?`
            );


        if (!confirmed) {
            return;
        }


        try {

            setError('');


            await api.put(
                `/roles/${role.id}/activate`
            );


            await loadRoles();


        } catch (err) {

            console.error(err);

            setError(
                err.response?.data?.message ||
                'Unable to activate role.'
            );

        }
    }


    // =====================================================
    // SUPERADMIN ONLY
    // =====================================================

    if (!isSuperAdmin) {
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
                        Roles
                    </h1>

                    <p className="muted">
                        Create and manage system roles.
                    </p>

                </div>


                <button
                    type="button"
                    className="btn btn-primary"
                    onClick={openCreate}
                >

                    <Plus size={16} />

                    Add Role

                </button>

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
                        Loading roles...
                    </p>


                ) : roles.length === 0 ? (

                    <div className="empty-state">

                        <p>
                            No roles found.
                        </p>


                        <button
                            className="btn btn-primary"
                            onClick={openCreate}
                        >

                            <Plus size={16} />

                            Add Role

                        </button>

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
                                        Role Code
                                    </th>

                                    <th>
                                        Role Name
                                    </th>

                                    <th>
                                        Description
                                    </th>

                                    <th>
                                        Sequence
                                    </th>

                                    <th>
                                        Status
                                    </th>

                                    <th>
                                        Actions
                                    </th>

                                </tr>

                            </thead>


                            <tbody>

                                {roles.map(
                                    (role, index) => {

                                        const isActive =
                                            Number(role.active) === 1;


                                        const isSuperAdminRole =
                                            String(
                                                role.role_code || ''
                                            ).toUpperCase() ===
                                            'SUPERADMIN';


                                        return (

                                            <tr
                                                key={role.id}
                                            >

                                                {/* # */}

                                                <td>
                                                    {index + 1}
                                                </td>


                                                {/* ROLE CODE */}

                                                <td>

                                                    <strong>
                                                        {role.role_code}
                                                    </strong>

                                                </td>


                                                {/* ROLE NAME */}

                                                <td>
                                                    {role.role_name}
                                                </td>


                                                {/* DESCRIPTION */}

                                                <td>
                                                    {role.description || '-'}
                                                </td>


                                                {/* SEQUENCE */}

                                                <td>
                                                    {role.sequence}
                                                </td>


                                                {/* STATUS */}

                                                <td>

                                                    <span
                                                        className={
                                                            isActive
                                                                ? 'badge badge-active'
                                                                : 'badge badge-inactive'
                                                        }
                                                    >

                                                        {isActive
                                                            ? 'ACTIVE'
                                                            : 'INACTIVE'}

                                                    </span>

                                                </td>


                                                {/* ACTIONS */}

                                                <td>

                                                    <div
                                                        style={{
                                                            display: 'flex',
                                                            gap: 6
                                                        }}
                                                    >

                                                        {/* EDIT */}

                                                        <button
                                                            type="button"
                                                            className="icon-btn"
                                                            title="Edit"
                                                            onClick={() =>
                                                                openEdit(
                                                                    role
                                                                )
                                                            }
                                                        >

                                                            <Pencil
                                                                size={15}
                                                            />

                                                        </button>


                                                        {/* DEACTIVATE */}

                                                        {isActive &&
                                                            !isSuperAdminRole && (

                                                                <button
                                                                    type="button"
                                                                    className="icon-btn icon-btn-danger"
                                                                    title="Deactivate"
                                                                    onClick={() =>
                                                                        handleDeactivate(
                                                                            role
                                                                        )
                                                                    }
                                                                >

                                                                    <Power
                                                                        size={15}
                                                                    />

                                                                </button>

                                                            )}


                                                        {/* ACTIVATE */}

                                                        {!isActive && (

                                                            <button
                                                                type="button"
                                                                className="icon-btn"
                                                                title="Activate"
                                                                onClick={() =>
                                                                    handleActivate(
                                                                        role
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

                                            </tr>

                                        );

                                    }
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


                        {/* HEADER */}

                        <div className="app-modal-header">

                            <h3>

                                {modal === 'edit'
                                    ? 'Edit Role'
                                    : 'Add Role'}

                            </h3>


                            <button
                                type="button"
                                className="app-modal-close"
                                onClick={closeModal}
                            >

                                <X size={18} />

                            </button>

                        </div>


                        {/* FORM */}

                        <form
                            onSubmit={handleSubmit}
                        >

                            <div className="app-modal-body">


                                {/* ROLE CODE */}

                                <label>
                                    Role Code *
                                </label>


                                <input
                                    type="text"
                                    maxLength={50}
                                    value={form.roleCode}
                                    onChange={(e) =>
                                        setForm(
                                            current => ({
                                                ...current,
                                                roleCode:
                                                    e.target.value
                                            })
                                        )
                                    }
                                    placeholder="Enter role code"
                                    required
                                />


                                {/* ROLE NAME */}

                                <label>
                                    Role Name *
                                </label>


                                <input
                                    type="text"
                                    maxLength={100}
                                    value={form.roleName}
                                    onChange={(e) =>
                                        setForm(
                                            current => ({
                                                ...current,
                                                roleName:
                                                    e.target.value
                                            })
                                        )
                                    }
                                    placeholder="Enter role name"
                                    required
                                />


                                {/* DESCRIPTION */}

                                <label>
                                    Description
                                </label>


                                <textarea
                                    maxLength={255}
                                    rows={3}
                                    value={form.description}
                                    onChange={(e) =>
                                        setForm(
                                            current => ({
                                                ...current,
                                                description:
                                                    e.target.value
                                            })
                                        )
                                    }
                                    placeholder="Enter role description"
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
                                            current => ({
                                                ...current,
                                                sequence:
                                                    e.target.value
                                            })
                                        )
                                    }
                                    required
                                />


                                {/* ERROR */}

                                {error && (

                                    <p className="error-text">
                                        {error}
                                    </p>

                                )}

                            </div>


                            {/* FOOTER */}

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