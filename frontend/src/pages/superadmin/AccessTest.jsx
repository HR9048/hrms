import usePageAccess from '../../hooks/usePageAccess';

export default function AccessTest() {
    const {
        permission,
        canView,
        canAdd,
        canEdit,
        canDelete,
        currentPath
    } = usePageAccess();

    return (
        <div className="page">

            <div className="page-toolbar">
                <div>
                    <h1>Access Test</h1>
                    <p className="muted">
                        Testing current page permissions.
                    </p>
                </div>
            </div>

            {/* Current URL */}

            <div className="card">
                <h3>Current Page</h3>

                <p>
                    <strong>Current URL:</strong>{' '}
                    {currentPath}
                </p>
            </div>

            {/* Permission */}

            <div className="card">
                <h3>Permission</h3>

                {permission ? (
                    <>
                        <p>
                            <strong>Menu ID:</strong>{' '}
                            {permission.menu_id}
                        </p>

                        <p>
                            <strong>Menu Key:</strong>{' '}
                            {permission.menu_key}
                        </p>

                        <p>
                            <strong>Route:</strong>{' '}
                            {permission.route_path}
                        </p>

                        <p>
                            <strong>View:</strong>{' '}
                            {canView ? 'ALLOWED' : 'DENIED'}
                        </p>

                        <p>
                            <strong>Add:</strong>{' '}
                            {canAdd ? 'ALLOWED' : 'DENIED'}
                        </p>

                        <p>
                            <strong>Edit:</strong>{' '}
                            {canEdit ? 'ALLOWED' : 'DENIED'}
                        </p>

                        <p>
                            <strong>Delete:</strong>{' '}
                            {canDelete ? 'ALLOWED' : 'DENIED'}
                        </p>
                    </>
                ) : (
                    <p className="muted">
                        No permission found for this page.
                    </p>
                )}
            </div>

            {/* Action buttons */}

            {canView && (
                <div className="card">
                    <h3>Actions</h3>

                    <div
                        style={{
                            display: 'flex',
                            gap: '10px',
                            flexWrap: 'wrap'
                        }}
                    >
                        {canAdd && (
                            <button
                                type="button"
                                className="btn btn-primary"
                                onClick={() =>
                                    alert('ADD permission works')
                                }
                            >
                                Add
                            </button>
                        )}

                        {canEdit && (
                            <button
                                type="button"
                                className="btn btn-primary"
                                onClick={() =>
                                    alert('EDIT permission works')
                                }
                            >
                                Edit
                            </button>
                        )}

                        {canDelete && (
                            <button
                                type="button"
                                className="btn btn-danger"
                                onClick={() =>
                                    alert('DELETE permission works')
                                }
                            >
                                Delete
                            </button>
                        )}

                        {!canAdd &&
                            !canEdit &&
                            !canDelete && (
                                <p className="muted">
                                    View-only access.
                                </p>
                            )}
                    </div>
                </div>
            )}

        </div>
    );
}