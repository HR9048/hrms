import { matchPath, useLocation, useNavigate } from 'react-router-dom';
import { useEffect, useMemo } from 'react';
import { useAccess } from '../context/AccessContext';

// Pages that are NOT menu items (they have :params, or are opened from another page) inherit access
// from a menu page instead of being looked up by their own URL.
//   path     route pattern, same syntax as in App.jsx
//   parent   route_path of the menu item whose permissions apply
//   requires permission the user needs on that parent to open the page: can_view | can_add | can_edit | can_delete
export const SUB_ROUTES = [
    { path: '/employee/:id/edit', parent: '/employee/All', requires: 'can_edit' }
];

export default function usePageAccess() {
    const access = useAccess() || [];
    const location = useLocation();
    const navigate = useNavigate();

    // Current browser path
    const currentPath = location.pathname;

    // Find the permission row for the current page (its own menu item, or the parent of a sub-page)
    const { permission, inherited } = useMemo(() => {
        const exact = access.find((item) => item.route_path === currentPath);
        if (exact) return { permission: exact, inherited: null };

        const sub = SUB_ROUTES.find((r) => matchPath({ path: r.path, end: true }, currentPath));
        const parent = sub ? access.find((item) => item.route_path === sub.parent) : null;

        return parent ? { permission: parent, inherited: sub } : { permission: null, inherited: null };
    }, [access, currentPath]);

    const has = (key) => Number(permission?.[key]) === 1;

    // a sub-page needs view on its parent AND the specific permission it declares
    const canView = inherited ? has('can_view') && has(inherited.requires) : has('can_view');
    const canAdd = has('can_add');
    const canEdit = has('can_edit');
    const canDelete = has('can_delete');

    // Redirect when page has no view permission
    useEffect(() => {
        // Don't redirect while access is still loading
        if (!access.length) {
            return;
        }

        if (!permission || !canView) {
            navigate('/dashboard', {
                replace: true
            });
        }
    }, [
        access,
        permission,
        canView,
        navigate
    ]);

    return {
        permission,
        canView,
        canAdd,
        canEdit,
        canDelete,
        currentPath
    };
}