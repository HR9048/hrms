import { useEffect, useState, useRef } from 'react';
import Swal from 'sweetalert2';
import api from '../../api/axios';

const HTTP_METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'];

const METHOD_BADGE = {
    GET: 'primary',
    POST: 'success',
    PUT: 'warning',
    PATCH: 'info',
    DELETE: 'danger'
};

const emptyForm = {
    apiUrl: '',
    httpMethods: [],
    loggedIn: false,
    rateLimit: 100,
    limitTime: 60
};

const isOn = value => value === true || Number(value) === 1;


// ============================================================
// HELPERS
// ============================================================

function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#039;');
}


// ============================================================
// ERROR HANDLING
// ============================================================

function showApiError(error, fallback) {

    const status = error?.response?.status;

    let message = error?.response?.data?.message;

    if (!message) {
        if (!error?.response) {
            message = 'Network error. Please check your connection.';
        } else if (status === 401) {
            message = 'Your session has expired. Please log in again.';
        } else if (status === 403) {
            message = 'You do not have permission to perform this action.';
        } else {
            message = fallback;
        }
    }

    let title = 'Error';
    let icon = 'error';

    if (status === 400) {
        title = 'Validation Error';
        icon = 'warning';
    } else if (status === 409) {
        title = 'Duplicate';
        icon = 'warning';
    } else if (status === 401 || status === 403) {
        title = 'Access Denied';
    } else if (status === 404) {
        title = 'Not Found';
    }

    Swal.fire(title, message, icon);
}


// ============================================================
// COMPONENT
// ============================================================

export default function ApiRateLimit() {

    const [records, setRecords] = useState([]);
    const [endpoints, setEndpoints] = useState([]);

    const [loading, setLoading] = useState(false);
    const [loadingEndpoints, setLoadingEndpoints] = useState(false);
    const [saving, setSaving] = useState(false);
    const [actionId, setActionId] = useState(null);

    const [showModal, setShowModal] = useState(false);
    const [editId, setEditId] = useState(null);

    const [form, setForm] = useState(emptyForm);

    // ============================================================
    // DATATABLE REFS
    // ============================================================

    const tableRef = useRef(null);
    const dataTableRef = useRef(null);
    const recordsRef = useRef([]);

    // Keep latest records available to DataTables event handlers
    useEffect(() => {
        recordsRef.current = records;
    }, [records]);


    // ============================================================
    // LOAD DATA
    // ============================================================

    const loadData = async () => {

        try {

            setLoading(true);

            const response = await api.get('/api-rate-limit');

            setRecords(
                Array.isArray(response.data)
                    ? response.data
                    : []
            );

        } catch (error) {

            showApiError(
                error,
                'Unable to load API rate limits.'
            );

        } finally {

            setLoading(false);
        }
    };


    const loadEndpoints = async () => {

        try {

            setLoadingEndpoints(true);

            const response = await api.get(
                '/api-rate-limit/endpoints'
            );

            setEndpoints(
                Array.isArray(response.data)
                    ? response.data
                    : []
            );

        } catch (error) {

            showApiError(
                error,
                'Unable to load API endpoints.'
            );

        } finally {

            setLoadingEndpoints(false);
        }
    };


    useEffect(() => {

        loadData();
        loadEndpoints();

    }, []);


    // ============================================================
    // FORM
    // ============================================================

    const isEdit = editId !== null;

    const selectedEndpoint = endpoints.find(
        e => e.apiUrl === form.apiUrl
    );


    // Active methods already configured
    const configuredByUrl = {};

    records
        .filter(r => isOn(r.isActive))
        .forEach(r => {

            const key = (
                r.apiUrl || ''
            ).toLowerCase();

            (
                configuredByUrl[key] =
                configuredByUrl[key] || []
            ).push(r.httpMethod);
        });


    const usedMethods = url =>
        configuredByUrl[
            (url || '').toLowerCase()
        ] || [];


    const usedForUrl = isEdit
        ? []
        : usedMethods(form.apiUrl);


    const supportedMethods = (
        selectedEndpoint
            ? HTTP_METHODS.filter(method =>
                selectedEndpoint.methods.includes(method)
            )
            : HTTP_METHODS
    ).filter(
        method => !usedForUrl.includes(method)
    );


    const availableEndpoints = endpoints.filter(ep =>

        isEdit

            ? (
                ep.apiUrl === form.apiUrl ||
                !usedMethods(ep.apiUrl)
                    .includes(form.httpMethods[0])
            )

            : ep.methods.some(
                method =>
                    !usedMethods(ep.apiUrl)
                        .includes(method)
            )
    );


    const allSelected =
        supportedMethods.length > 0 &&
        supportedMethods.every(
            method =>
                form.httpMethods.includes(method)
        );


    const urlOptions =
        form.apiUrl && !selectedEndpoint

            ? [
                {
                    apiUrl: form.apiUrl,
                    methods: HTTP_METHODS
                },
                ...availableEndpoints
            ]

            : availableEndpoints;


    // ============================================================
    // FORM FUNCTIONS
    // ============================================================

    const resetForm = () => {

        setForm(emptyForm);
        setEditId(null);

    };


    const openAdd = () => {

        resetForm();

        setShowModal(true);

        if (
            endpoints.length === 0 &&
            !loadingEndpoints
        ) {
            loadEndpoints();
        }
    };


    const openEdit = record => {

        setEditId(record.id);

        setForm({

            apiUrl: record.apiUrl || '',

            httpMethods: [
                record.httpMethod
            ],

            loggedIn: isOn(record.loggedIn),

            rateLimit:
                record.rateLimit || 100,

            limitTime:
                record.limitTime || 60
        });

        setShowModal(true);

        if (
            endpoints.length === 0 &&
            !loadingEndpoints
        ) {
            loadEndpoints();
        }
    };


    const closeModal = () => {

        if (saving) return;

        setShowModal(false);

        resetForm();
    };


    const handleChange = e => {

        const {
            name,
            value
        } = e.target;

        setForm(prev => ({
            ...prev,
            [name]: value
        }));
    };


    const handleUrlChange = e => {

        const apiUrl = e.target.value;

        const endpoint =
            endpoints.find(
                x => x.apiUrl === apiUrl
            );

        setForm(prev => ({

            ...prev,

            apiUrl,

            httpMethods:

                isEdit || !endpoint

                    ? prev.httpMethods

                    : prev.httpMethods.filter(
                        method =>
                            endpoint.methods.includes(method) &&
                            !usedMethods(apiUrl)
                                .includes(method)
                    )
        }));
    };


    const toggleMethod = method => {

        setForm(prev => ({

            ...prev,

            httpMethods:

                prev.httpMethods.includes(method)

                    ? prev.httpMethods.filter(
                        m => m !== method
                    )

                    : [
                        ...prev.httpMethods,
                        method
                    ]
        }));
    };


    const toggleAllMethods = () => {

        setForm(prev => ({

            ...prev,

            httpMethods:
                allSelected
                    ? []
                    : [...supportedMethods]
        }));
    };


    // ============================================================
    // SAVE
    // ============================================================

    const handleSubmit = async e => {

        e.preventDefault();

        if (saving) return;


        if (!form.apiUrl.trim()) {

            Swal.fire(
                'Required',
                'Please select an API URL.',
                'warning'
            );

            return;
        }


        if (form.httpMethods.length === 0) {

            Swal.fire(
                'Required',
                'Please select at least one HTTP method.',
                'warning'
            );

            return;
        }


        const rateLimit =
            Number(form.rateLimit);

        const limitTime =
            Number(form.limitTime);


        if (
            form.rateLimit === '' ||
            !Number.isInteger(rateLimit) ||
            rateLimit <= 0
        ) {

            Swal.fire(
                'Required',
                'Rate limit must be a whole number greater than 0.',
                'warning'
            );

            return;
        }


        if (
            form.limitTime === '' ||
            !Number.isInteger(limitTime) ||
            limitTime <= 0
        ) {

            Swal.fire(
                'Required',
                'Time window must be a whole number greater than 0.',
                'warning'
            );

            return;
        }


        try {

            setSaving(true);


            // ============================
            // EDIT
            // ============================

            if (isEdit) {

                await api.put(
                    `/api-rate-limit/${editId}`,
                    {
                        apiUrl:
                            form.apiUrl.trim(),

                        httpMethod:
                            form.httpMethods[0],

                        loggedIn:
                            Boolean(form.loggedIn),

                        rateLimit,

                        limitTime
                    }
                );


                await Swal.fire(
                    'Updated',
                    'API rate limit updated successfully.',
                    'success'
                );


            }

            // ============================
            // CREATE
            // ============================

            else {

                const response =
                    await api.post(
                        '/api-rate-limit',
                        {
                            apiUrl:
                                form.apiUrl.trim(),

                            httpMethods:
                                form.httpMethods,

                            loggedIn:
                                Boolean(form.loggedIn),

                            rateLimit,

                            limitTime
                        }
                    );


                await Swal.fire(
                    'Added',
                    response?.data?.message ||
                    'API rate limit added successfully.',
                    'success'
                );
            }


            setShowModal(false);

            resetForm();

            await loadData();


        } catch (error) {

            showApiError(
                error,
                'Unable to save API rate limit.'
            );

        } finally {

            setSaving(false);
        }
    };


    // ============================================================
    // DELETE
    // ============================================================

    const handleDelete = async record => {

        if (actionId !== null) return;


        const result =
            await Swal.fire({

                title:
                    'Delete API rate limit?',

                text:
                    `${record.httpMethod} ${record.apiUrl} will be deactivated.`,

                icon:
                    'warning',

                showCancelButton:
                    true,

                confirmButtonText:
                    'Delete',

                cancelButtonText:
                    'Cancel'
            });


        if (!result.isConfirmed) return;


        try {

            setActionId(record.id);


            await api.delete(
                `/api-rate-limit/${record.id}`
            );


            await Swal.fire(
                'Deleted',
                'API rate limit deleted successfully.',
                'success'
            );


            await loadData();


        } catch (error) {

            showApiError(
                error,
                'Unable to delete API rate limit.'
            );

        } finally {

            setActionId(null);
        }
    };


    // ============================================================
    // ACTIVATE
    // ============================================================

    const handleActivate = async record => {

        if (actionId !== null) return;


        try {

            setActionId(record.id);


            await api.put(
                `/api-rate-limit/${record.id}/activate`
            );


            await Swal.fire(
                'Activated',
                'API rate limit activated successfully.',
                'success'
            );


            await loadData();


        } catch (error) {

            showApiError(
                error,
                'Unable to activate API rate limit.'
            );

        } finally {

            setActionId(null);
        }
    };


    // ============================================================
    // DATATABLE INITIALIZATION
    // ============================================================

    useEffect(() => {

        const $ = window.jQuery;


        if (
            !$ ||
            !$.fn ||
            !$.fn.DataTable ||
            !tableRef.current
        ) {
            return;
        }


        // Prevent duplicate initialization
        if (
            $.fn.DataTable.isDataTable(
                tableRef.current
            )
        ) {
            return;
        }


        const table =
            $(tableRef.current).DataTable({

                pageLength: 10,

                lengthMenu: [
                    [5, 10, 25, 50, 100],
                    [5, 10, 25, 50, 100]
                ],

                searching: true,

                ordering: true,

                paging: true,

                info: true,

                autoWidth: false,

                order: [
                    [0, 'asc']
                ],

                columnDefs: [

                    {
                        orderable: false,
                        searchable: false,
                        targets: [7]
                    }

                ]
            });


        dataTableRef.current = table;


        // ========================================================
        // EDIT BUTTON
        // ========================================================

        $(tableRef.current).on(
            'click',
            '.js-edit',
            function () {

                const id =
                    Number(
                        $(this).attr('data-id')
                    );


                const record =
                    recordsRef.current.find(
                        r => Number(r.id) === id
                    );


                if (record) {
                    openEdit(record);
                }
            }
        );


        // ========================================================
        // DELETE BUTTON
        // ========================================================

        $(tableRef.current).on(
            'click',
            '.js-delete',
            function () {

                const id =
                    Number(
                        $(this).attr('data-id')
                    );


                const record =
                    recordsRef.current.find(
                        r => Number(r.id) === id
                    );


                if (record) {
                    handleDelete(record);
                }
            }
        );


        // ========================================================
        // ACTIVATE BUTTON
        // ========================================================

        $(tableRef.current).on(
            'click',
            '.js-activate',
            function () {

                const id =
                    Number(
                        $(this).attr('data-id')
                    );


                const record =
                    recordsRef.current.find(
                        r => Number(r.id) === id
                    );


                if (record) {
                    handleActivate(record);
                }
            }
        );


        // Cleanup
        return () => {

            if (
                $.fn.DataTable.isDataTable(
                    tableRef.current
                )
            ) {

                $(tableRef.current).off(
                    'click',
                    '.js-edit'
                );

                $(tableRef.current).off(
                    'click',
                    '.js-delete'
                );

                $(tableRef.current).off(
                    'click',
                    '.js-activate'
                );


                table.destroy();

                dataTableRef.current = null;
            }
        };

    }, []);


    // ============================================================
    // UPDATE DATATABLE DATA
    // ============================================================

    useEffect(() => {

        const table =
            dataTableRef.current;


        if (!table) return;


        const rows =
            records.map(
                (record, index) => {

                    const active =
                        isOn(record.isActive);

                    const busy =
                        actionId === record.id;


                    // --------------------------------------------
                    // ACTION BUTTONS
                    // --------------------------------------------

                    let actionHtml = '';


                    if (active) {

                        actionHtml = `

                            <button
                                type="button"
                                class="btn btn-sm btn-outline-primary me-2 js-edit"
                                data-id="${record.id}"
                                title="Edit"
                                ${actionId !== null ? 'disabled' : ''}
                            >
                                <i class="bi bi-pencil"></i>
                            </button>

                            <button
                                type="button"
                                class="btn btn-sm btn-outline-danger js-delete"
                                data-id="${record.id}"
                                title="Delete"
                                ${actionId !== null ? 'disabled' : ''}
                            >
                                ${
                                    busy
                                        ? '<span class="spinner-border spinner-border-sm"></span>'
                                        : '<i class="bi bi-trash"></i>'
                                }
                            </button>

                        `;

                    } else {

                        actionHtml = `

                            <button
                                type="button"
                                class="btn btn-sm btn-outline-success js-activate"
                                data-id="${record.id}"
                                title="Activate"
                                ${actionId !== null ? 'disabled' : ''}
                            >

                                ${
                                    busy
                                        ? '<span class="spinner-border spinner-border-sm me-1"></span>'
                                        : '<i class="bi bi-arrow-counterclockwise me-1"></i>'
                                }

                                Activate

                            </button>

                        `;
                    }


                    // --------------------------------------------
                    // METHOD BADGE
                    // --------------------------------------------

                    const methodBadge =
                        METHOD_BADGE[
                            record.httpMethod
                        ] || 'secondary';


                    // --------------------------------------------
                    // LIMIT TYPE
                    // --------------------------------------------

                    const limitType =
                        isOn(record.loggedIn)

                            ? `
                                <span class="badge text-bg-success">
                                    <i class="bi bi-person-fill me-1"></i>
                                    User
                                </span>
                              `

                            : `
                                <span class="badge text-bg-info">
                                    <i class="bi bi-globe me-1"></i>
                                    IP
                                </span>
                              `;


                    // --------------------------------------------
                    // STATUS
                    // --------------------------------------------

                    const statusHtml = `

                        <span class="badge ${
                            active
                                ? 'text-bg-success'
                                : 'text-bg-secondary'
                        }">

                            ${
                                active
                                    ? 'Active'
                                    : 'Inactive'
                            }

                        </span>

                    `;


                    return [

                        index + 1,

                        `<code>${escapeHtml(
                            record.apiUrl
                        )}</code>`,

                        `
                        <span class="badge text-bg-${methodBadge}">
                            ${escapeHtml(
                                record.httpMethod
                            )}
                        </span>
                        `,

                        limitType,

                        `${escapeHtml(
                            record.rateLimit
                        )} requests`,

                        `${escapeHtml(
                            record.limitTime
                        )} sec`,

                        statusHtml,

                        actionHtml
                    ];
                }
            );


        // Update rows WITHOUT React touching tbody
        table.clear();

        table.rows.add(rows);

        table.draw(false);


    }, [records, actionId]);


    // ============================================================
    // RENDER
    // ============================================================

    return (

        <div className="container-fluid">


            {/* ====================================================
                HEADER
            ==================================================== */}

            <div className="d-flex justify-content-between align-items-center mb-4">

                <div>

                    <h4 className="mb-1">
                        API Rate Limit
                    </h4>

                    <p className="text-muted mb-0">
                        Manage API request limits.
                    </p>

                </div>


                <button
                    type="button"
                    className="btn btn-primary"
                    onClick={openAdd}
                >

                    <i className="bi bi-plus-lg me-2" />

                    Add API Rate Limit

                </button>

            </div>


            {/* ====================================================
                TABLE CARD
            ==================================================== */}

            <div className="card">

                <div className="card-body">


                    {/* Loading indicator */}

                    {loading && (

                        <div className="text-center py-3">

                            <div
                                className="spinner-border spinner-border-sm me-2"
                                role="status"
                            />

                            Loading API rate limits...

                        </div>

                    )}


                    {/* =================================================
                        IMPORTANT:

                        The table is ALWAYS rendered.

                        Do NOT conditionally remove the table because
                        DataTables owns its tbody.
                    ================================================== */}

                    <div className="table-responsive">

                        <table
                            ref={tableRef}
                            className="table table-hover align-middle"
                            style={{
                                width: '100%'
                            }}
                        >

                            <thead>

                                <tr>

                                    <th>#</th>

                                    <th>
                                        API URL
                                    </th>

                                    <th>
                                        Method
                                    </th>

                                    <th>
                                        Limit Type
                                    </th>

                                    <th>
                                        Rate Limit
                                    </th>

                                    <th>
                                        Window
                                    </th>

                                    <th>
                                        Status
                                    </th>

                                    <th>
                                        Action
                                    </th>

                                </tr>

                            </thead>


                            {/*

                                VERY IMPORTANT:

                                React does NOT render records here.

                                DataTables manages this tbody.

                            */}

                            <tbody />

                        </table>

                    </div>


                    {/* No records message */}

                    {!loading &&
                        records.length === 0 && (

                            <div className="text-center text-muted py-4">

                                <i
                                    className="bi bi-shield-slash fs-1"
                                />

                                <p className="mt-3 mb-0">

                                    No API rate-limit
                                    records found.

                                </p>

                            </div>

                        )}

                </div>

            </div>


            {/* ====================================================
                MODAL
            ==================================================== */}

            {showModal && (

                <div
                    className="modal d-block"
                    tabIndex="-1"
                    style={{
                        backgroundColor:
                            'rgba(0,0,0,0.5)'
                    }}
                >

                    <div className="modal-dialog modal-lg modal-dialog-centered modal-dialog-scrollable">

                        <div className="modal-content">


                            {/* ===============================
                                MODAL HEADER
                            ================================ */}

                            <div className="modal-header">

                                <h5 className="modal-title">

                                    {
                                        isEdit
                                            ? 'Edit API Rate Limit'
                                            : 'Add API Rate Limit'
                                    }

                                </h5>


                                <button
                                    type="button"
                                    className="btn-close"
                                    onClick={closeModal}
                                    disabled={saving}
                                />

                            </div>


                            {/* ===============================
                                FORM
                            ================================ */}

                            <form
                                onSubmit={handleSubmit}
                                className="d-flex flex-column overflow-hidden"
                            >


                                <div className="modal-body">


                                    {/* =================================================
                                        API URL
                                    ================================================== */}

                                    <div className="mb-3">

                                        <label className="form-label">

                                            API URL

                                            <span className="text-danger">
                                                {' '}*
                                            </span>

                                        </label>


                                        <select
                                            name="apiUrl"
                                            className="form-select"
                                            value={form.apiUrl}
                                            onChange={handleUrlChange}
                                            disabled={
                                                saving ||
                                                loadingEndpoints
                                            }
                                        >

                                            <option value="">

                                                {
                                                    loadingEndpoints

                                                        ? 'Loading endpoints...'

                                                        : urlOptions.length === 0

                                                            ? 'All endpoints are already configured'

                                                            : 'Select API URL...'
                                                }

                                            </option>


                                            {urlOptions.map(ep => (

                                                <option
                                                    key={ep.apiUrl}
                                                    value={ep.apiUrl}
                                                >

                                                    {ep.apiUrl}

                                                </option>

                                            ))}

                                        </select>


                                        {selectedEndpoint && (

                                            <small className="text-muted">

                                                Supported by this endpoint:

                                                {' '}

                                                {
                                                    selectedEndpoint.methods.join(
                                                        ', '
                                                    )
                                                }

                                            </small>

                                        )}

                                    </div>


                                    {/* =================================================
                                        HTTP METHODS
                                    ================================================== */}

                                    <div className="mb-3">


                                        <div className="d-flex justify-content-between align-items-center mb-2">

                                            <label className="form-label mb-0">

                                                HTTP {
                                                    isEdit
                                                        ? 'Method'
                                                        : 'Methods'
                                                }

                                                {!isEdit && (

                                                    <span className="text-danger">
                                                        {' '}*
                                                    </span>

                                                )}

                                            </label>


                                            {!isEdit && (

                                                <button
                                                    type="button"
                                                    className="btn btn-sm btn-outline-secondary"
                                                    onClick={
                                                        toggleAllMethods
                                                    }
                                                    disabled={
                                                        saving ||
                                                        supportedMethods.length === 0
                                                    }
                                                >

                                                    {
                                                        allSelected
                                                            ? 'Unselect All'
                                                            : 'Select All'
                                                    }

                                                </button>

                                            )}

                                        </div>


                                        {isEdit ? (

                                            <div>

                                                <span
                                                    className={`
                                                        badge
                                                        text-bg-${
                                                            METHOD_BADGE[
                                                                form.httpMethods[0]
                                                            ] || 'secondary'
                                                        }
                                                        fs-6
                                                    `}
                                                >

                                                    {form.httpMethods[0]}

                                                </span>


                                                <div>

                                                    <small className="text-muted">

                                                        The HTTP method cannot
                                                        be changed. Create a new
                                                        record for another method.

                                                    </small>

                                                </div>

                                            </div>

                                        ) : (

                                            <div className="d-flex flex-wrap gap-3">

                                                {HTTP_METHODS.map(
                                                    method => {

                                                        const supported =
                                                            supportedMethods.includes(
                                                                method
                                                            );

                                                        const configured =
                                                            usedForUrl.includes(
                                                                method
                                                            );


                                                        return (

                                                            <div
                                                                className="form-check"
                                                                key={method}
                                                            >

                                                                <input
                                                                    type="checkbox"
                                                                    className="form-check-input"
                                                                    id={`method-${method}`}
                                                                    checked={
                                                                        form.httpMethods.includes(
                                                                            method
                                                                        )
                                                                    }
                                                                    onChange={() =>
                                                                        toggleMethod(
                                                                            method
                                                                        )
                                                                    }
                                                                    disabled={
                                                                        saving ||
                                                                        !supported
                                                                    }
                                                                />


                                                                <label
                                                                    className="form-check-label"
                                                                    htmlFor={`method-${method}`}
                                                                >

                                                                    {method}


                                                                    {configured && (

                                                                        <span className="text-muted small">

                                                                            {' '}
                                                                            (configured)

                                                                        </span>

                                                                    )}

                                                                </label>

                                                            </div>

                                                        );

                                                    }
                                                )}

                                            </div>

                                        )}

                                    </div>


                                    {/* =================================================
                                        RATE LIMIT TYPE
                                    ================================================== */}

                                    <div className="mb-3">

                                        <label className="form-label">

                                            Rate Limit Type

                                        </label>


                                        <div className="row g-2">


                                            {/* IP */}

                                            <div className="col-md-6">

                                                <input
                                                    type="radio"
                                                    className="btn-check"
                                                    name="limitType"
                                                    id="limitType-ip"
                                                    checked={
                                                        !form.loggedIn
                                                    }
                                                    onChange={() =>
                                                        setForm(prev => ({
                                                            ...prev,
                                                            loggedIn: false
                                                        }))
                                                    }
                                                    disabled={saving}
                                                />


                                                <label
                                                    className="btn btn-outline-primary w-100 text-start p-3"
                                                    htmlFor="limitType-ip"
                                                >

                                                    <i className="bi bi-globe fs-4 me-2 align-middle" />

                                                    <strong>
                                                        IP Based
                                                    </strong>


                                                    <div className="small">

                                                        Limits requests
                                                        per IP address

                                                    </div>

                                                </label>

                                            </div>


                                            {/* USER */}

                                            <div className="col-md-6">

                                                <input
                                                    type="radio"
                                                    className="btn-check"
                                                    name="limitType"
                                                    id="limitType-user"
                                                    checked={
                                                        form.loggedIn
                                                    }
                                                    onChange={() =>
                                                        setForm(prev => ({
                                                            ...prev,
                                                            loggedIn: true
                                                        }))
                                                    }
                                                    disabled={saving}
                                                />


                                                <label
                                                    className="btn btn-outline-primary w-100 text-start p-3"
                                                    htmlFor="limitType-user"
                                                >

                                                    <i className="bi bi-person-fill fs-4 me-2 align-middle" />

                                                    <strong>
                                                        User Based
                                                    </strong>


                                                    <div className="small">

                                                        Limits requests
                                                        per logged-in user

                                                    </div>

                                                </label>

                                            </div>

                                        </div>

                                    </div>


                                    {/* =================================================
                                        RATE LIMIT + TIME
                                    ================================================== */}

                                    <div className="row">


                                        {/* RATE LIMIT */}

                                        <div className="col-md-6 mb-3">

                                            <label className="form-label">

                                                Rate Limit

                                                <span className="text-danger">
                                                    {' '}*
                                                </span>

                                            </label>


                                            <div className="input-group">

                                                <input
                                                    type="number"
                                                    name="rateLimit"
                                                    className="form-control"
                                                    min="1"
                                                    step="1"
                                                    value={
                                                        form.rateLimit
                                                    }
                                                    onChange={
                                                        handleChange
                                                    }
                                                    disabled={
                                                        saving
                                                    }
                                                />


                                                <span className="input-group-text">

                                                    requests

                                                </span>

                                            </div>


                                            <small className="text-muted">

                                                Maximum number
                                                of requests.

                                            </small>

                                        </div>


                                        {/* TIME WINDOW */}

                                        <div className="col-md-6 mb-3">

                                            <label className="form-label">

                                                Time Window

                                                <span className="text-danger">
                                                    {' '}*
                                                </span>

                                            </label>


                                            <div className="input-group">

                                                <input
                                                    type="number"
                                                    name="limitTime"
                                                    className="form-control"
                                                    min="1"
                                                    step="1"
                                                    value={
                                                        form.limitTime
                                                    }
                                                    onChange={
                                                        handleChange
                                                    }
                                                    disabled={
                                                        saving
                                                    }
                                                />


                                                <span className="input-group-text">

                                                    seconds

                                                </span>

                                            </div>


                                            <small className="text-muted">

                                                Time window used by
                                                your rate-limit service.

                                            </small>

                                        </div>

                                    </div>

                                </div>


                                {/* =================================================
                                    MODAL FOOTER
                                ================================================== */}

                                <div className="modal-footer">


                                    <button
                                        type="button"
                                        className="btn btn-light"
                                        onClick={closeModal}
                                        disabled={saving}
                                    >

                                        Cancel

                                    </button>


                                    <button
                                        type="submit"
                                        className="btn btn-primary"
                                        disabled={saving}
                                    >

                                        {saving ? (

                                            <>

                                                <span className="spinner-border spinner-border-sm me-2" />

                                                {
                                                    isEdit
                                                        ? 'Updating...'
                                                        : 'Saving...'
                                                }

                                            </>

                                        ) : (

                                            <>

                                                <i className="bi bi-save me-2" />

                                                {
                                                    isEdit
                                                        ? 'Update'
                                                        : 'Save'
                                                }

                                            </>

                                        )}

                                    </button>

                                </div>

                            </form>

                        </div>

                    </div>

                </div>

            )}

        </div>
    );
}