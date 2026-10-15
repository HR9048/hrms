import { useEffect, useState } from 'react';
import { useNavigate, useLocation, matchPath } from 'react-router-dom';
import { SUB_ROUTES } from '../hooks/usePageAccess';
import { ChevronLeft, ChevronRight, LogOut, Menu, X } from 'lucide-react';
import api from '../api/axios';
import { useAuth } from '../context/AuthContext';
import { useLanguage } from '../context/LanguageContext';
import MenuItem from './MenuItem.jsx';
import AccountSwitcher from './AccountSwitcher.jsx';
import { AccessProvider } from '../context/AccessContext';

export default function Navbar({ children }) {
  const { account, logout } = useAuth();
  const { langCode, setLangCode, t } = useLanguage();
  const navigate = useNavigate();
  const location = useLocation();

  const [languages, setLanguages] = useState([]);
  const [menuTree, setMenuTree] = useState([]);
  const [access, setAccess] = useState([]);
  const [accessLoading, setAccessLoading] = useState(true);
  const [collapsed, setCollapsed] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);

  useEffect(() => {
    api.get('/languages').then((res) => setLanguages(res.data));
  }, []);

  // Re-fetch the nav tree whenever the language toggles (labels change) -
  // this IS the "toggle language changes navbar language live" requirement.
  useEffect(() => {
    api.get(`/menus?lang=${langCode}`).then((res) => setMenuTree(res.data));
  }, [langCode, account?.accountId]);

  // Close the mobile drawer whenever the route changes (e.g. back button,
  // or a Link click that MenuItem didn't already close for us).
  useEffect(() => {
    setMobileOpen(false);
  }, [location.pathname]);

  // Lock background scroll while the mobile drawer is open.
  useEffect(() => {
    document.body.style.overflow = mobileOpen ? 'hidden' : '';
    return () => { document.body.style.overflow = ''; };
  }, [mobileOpen]);

  useEffect(() => {
    if (!account?.accountId) {
      setAccess([]);
      setAccessLoading(false);
      return;
    }

    setAccessLoading(true);

    api.get('/menus/my-access').then((res) => {setAccess(res.data || []);}).catch((err) => {
        console.error('Failed to load menu permissions:', err);
        setAccess([]);
      })
      .finally(() => {setAccessLoading(false);});
  }, [account?.accountId]);

  useEffect(() => {
    if (!account?.accountId || accessLoading) {
      return;
    }

    const currentPath = normalizePath(location.pathname);
    //console.log('Checking access for path:', currentPath, 'against access data:', access);

    // Always allow dashboard itself
    if (currentPath === '/dashboard') {
      return;
    }

    const permission = findPermission(currentPath);

    // Current URL doesn't exist in the user's access data
    // OR the user does not have view permission.
    if (!permission || Number(permission.can_view) !== 1) {
      navigate('/dashboard', { replace: true });
    }
  }, [
    location.pathname,
    access,
    accessLoading,
    account?.accountId
  ]);

  function handleLanguageChange(code) {
    localStorage.setItem('hrms_lang_manual', '1');
    setLangCode(code);
  }

  function handleLogout() {
    logout();
    navigate('/login');
  }
  function normalizePath(path) {
    if (!path) return '';

    const cleanPath = path.split('?')[0].split('#')[0];

    if (cleanPath.length > 1) {
      return cleanPath.replace(/\/+$/, '');
    }

    return cleanPath || '/';
  }

    function findPermission(path) {
    const currentPath = normalizePath(path);

    if (!currentPath) {
      return null;
    }

    const same = (a, b) => normalizePath(a).toLowerCase() === normalizePath(b).toLowerCase();

    // First try exact route match
    const exactMatch = access.find((item) => same(item.route_path, currentPath));

    if (exactMatch) {
      return exactMatch;
    }

    // Declared sub-pages (not menu items), e.g. /employee/15/edit: access comes from their parent menu page
    // (the list lives in hooks/usePageAccess.js). They need can_view AND the permission the sub-page declares.
    const sub = SUB_ROUTES.find((r) => matchPath({ path: r.path, end: true, caseSensitive: false }, currentPath));
    if (sub) {
      const parent = access.find((item) => same(item.route_path, sub.parent));

      if (account?.roleCode === 'SUPERADMIN') {
        return { ...(parent || {}), can_view: 1 };
      }
      if (!parent) {
        return null;
      }

      const allowed = Number(parent.can_view) === 1 && Number(parent[sub.requires]) === 1;
      return { ...parent, can_view: allowed ? 1 : 0 };
    }

    // Support child/dynamic routes such as:
    // /employees/15
    // /employees/edit/15
    const parentMatches = access
      .filter((item) => {
        const route = normalizePath(item.route_path);

        return (
          route &&
          route !== '/' &&
          currentPath.startsWith(`${route}/`)
        );
      })
      .sort(
        (a, b) =>
          normalizePath(b.route_path).length -
          normalizePath(a.route_path).length
      );

    return parentMatches[0] || null;
  }
  return (
    <div className={`app-shell ${collapsed ? 'shell-collapsed' : ''} ${mobileOpen ? 'sidebar-open' : ''}`}>
      <header className="navbar">
        <button
          type="button"
          className="hamburger-btn"
          onClick={() => setMobileOpen((o) => !o)}
          aria-label="Toggle menu"
        >
          <Menu size={22} />
        </button>

        <div className="navbar-brand">
          <span className="brand-badge">HR</span>
          <div className="navbar-brand-text">
            <span className="navbar-brand-short">HRMS</span>
            <span className="navbar-brand-full">Human Resource Management System</span>
          </div>
        </div>
      </header>

      {mobileOpen && (
        <div
          className="sidebar-backdrop"
          onClick={() => setMobileOpen(false)}
          aria-hidden="true"
        />
      )}

      <aside className={`sidebar ${mobileOpen ? 'sidebar-open' : ''}`}>
        <div className="sidebar-top-row">
          <button
            type="button"
            className="sidebar-toggle"
            onClick={() => setCollapsed((c) => !c)}
            aria-label="Toggle sidebar"
          >
            {collapsed ? <ChevronRight size={16} /> : <ChevronLeft size={16} />}
          </button>

          <button
            type="button"
            className="sidebar-close"
            onClick={() => setMobileOpen(false)}
            aria-label="Close menu"
          >
            <X size={18} />
          </button>
        </div>

        <div className="sidebar-account">
          <AccountSwitcher collapsed={collapsed} />
          {languages.length > 1 && !collapsed && (
            <select className="select-pill" value={langCode} onChange={(e) => handleLanguageChange(e.target.value)}>
              {languages.map((l) => <option key={l.code} value={l.code}>{l.name}</option>)}
            </select>
          )}
        </div>

        {!collapsed && (
          <div className="sidebar-user">
            <span>{t('welcome')}, {account?.employeeName}</span>
            <span className="sidebar-user-role">{account?.roleName}</span>
          </div>
        )}

        <nav className="sidebar-menu">
          <ul className="menu-col">
            {menuTree.map((node) => (
              <MenuItem
                key={node.id}
                node={node}
                depth={0}
                collapsed={collapsed}
                onNavigate={() => setMobileOpen(false)}
              />
            ))}
          </ul>
        </nav>

        <button className="btn btn-ghost sidebar-logout" onClick={handleLogout}>
          <LogOut size={18} />
          {!collapsed && t('logout')}
        </button>
      </aside>

      <main className="app-content">
        <AccessProvider
          access={access}
        >
          {children}
        </AccessProvider>
      </main>
    </div>
  );
}