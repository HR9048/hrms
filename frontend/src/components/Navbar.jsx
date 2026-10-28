import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useLocation, matchPath } from 'react-router-dom';
import { SUB_ROUTES } from '../hooks/usePageAccess';
import { ChevronLeft, ChevronRight, Globe, LogOut, Menu, X } from 'lucide-react';
import api from '../api/axios';
import { useAuth } from '../context/AuthContext';
import { useLanguage } from '../context/LanguageContext';
import MenuItem from './MenuItem.jsx';
import AccountSwitcher from './AccountSwitcher.jsx';
import { AccessProvider } from '../context/AccessContext';
import './Shell.css';

const SB_MIN = 200;
const SB_MAX = 420;
const SB_DEFAULT = 240;
const SB_COLLAPSED = 68;
const KEY_W = 'hrms_sidebar_w';
const KEY_COLLAPSED = 'hrms_sidebar_collapsed';
const MOBILE_QUERY = '(max-width: 768px)';

const clampWidth = (w) => Math.max(SB_MIN, Math.min(w, SB_MAX, Math.round(window.innerWidth * 0.5)));

function readWidth() {
  try {
    const v = Number(localStorage.getItem(KEY_W));
    return v ? clampWidth(v) : SB_DEFAULT;
  } catch {
    return SB_DEFAULT;
  }
}

function readCollapsed() {
  try {
    return localStorage.getItem(KEY_COLLAPSED) === '1';
  } catch {
    return false;
  }
}

function normalizePath(path) {
  if (!path) return '';

  const cleanPath = path.split('?')[0].split('#')[0];

  if (cleanPath.length > 1) {
    return cleanPath.replace(/\/+$/, '');
  }

  return cleanPath || '/';
}

function useMediaQuery(query) {
  const [matches, setMatches] = useState(() => window.matchMedia(query).matches);

  useEffect(() => {
    const mq = window.matchMedia(query);
    const onChange = (e) => setMatches(e.matches);
    setMatches(mq.matches);
    mq.addEventListener('change', onChange);
    return () => mq.removeEventListener('change', onChange);
  }, [query]);

  return matches;
}

export default function Navbar({ children }) {
  const { account, logout } = useAuth();
  const { langCode, setLangCode, t } = useLanguage();
  const navigate = useNavigate();
  const location = useLocation();
  const isMobile = useMediaQuery(MOBILE_QUERY);

  const [languages, setLanguages] = useState([]);
  const [menuTree, setMenuTree] = useState([]);
  const [access, setAccess] = useState([]);
  const [accessLoading, setAccessLoading] = useState(true);
  const [collapsed, setCollapsed] = useState(readCollapsed);
  const [sbWidth, setSbWidth] = useState(readWidth);
  const [resizing, setResizing] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);

  // collapsing / resizing only applies to the desktop sidebar; on mobile it is a drawer
  const effCollapsed = collapsed && !isMobile;

  useEffect(() => {
    api.get('/languages').then((res) => setLanguages(res.data));
  }, []);

  // Re-fetch the nav tree whenever the language toggles (labels change) -
  // this IS the "toggle language changes navbar language live" requirement.
  useEffect(() => {
    api.get(`/menus?lang=${langCode}`).then((res) => setMenuTree(res.data));
  }, [langCode, account?.accountId]);

  // remember the user's sidebar choices
  useEffect(() => {
    try { localStorage.setItem(KEY_COLLAPSED, collapsed ? '1' : '0'); } catch { /* storage unavailable */ }
  }, [collapsed]);

  useEffect(() => {
    try { localStorage.setItem(KEY_W, String(sbWidth)); } catch { /* storage unavailable */ }
  }, [sbWidth]);

  // Close the mobile drawer whenever the route changes (e.g. back button,
  // or a Link click that MenuItem didn't already close for us).
  useEffect(() => {
    setMobileOpen(false);
  }, [location.pathname]);

  useEffect(() => {
    if (!isMobile) setMobileOpen(false);
  }, [isMobile]);

  // Esc closes the drawer
  useEffect(() => {
    if (!mobileOpen) return undefined;
    const onKey = (e) => { if (e.key === 'Escape') setMobileOpen(false); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [mobileOpen]);

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

    api.get('/menus/my-access').then((res) => { setAccess(res.data || []); }).catch((err) => {
      console.error('Failed to load menu permissions:', err);
      setAccess([]);
    })
      .finally(() => { setAccessLoading(false); });
  }, [account?.accountId]);

  useEffect(() => {
    if (!account?.accountId || accessLoading) {
      return;
    }

    const currentPath = normalizePath(location.pathname);

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

  // The menu item that stands for the current page (sub-pages map to their parent menu item).
  const activePath = useMemo(() => {
    const current = normalizePath(location.pathname).toLowerCase();

    const sub = SUB_ROUTES.find((r) => matchPath({ path: r.path, end: true, caseSensitive: false }, location.pathname));
    if (sub) return normalizePath(sub.parent).toLowerCase();

    const leaves = [];
    const walk = (nodes) => nodes.forEach((n) => {
      if (n.children && n.children.length) walk(n.children);
      else if (n.path) leaves.push(normalizePath(n.path).toLowerCase());
    });
    walk(menuTree);

    return leaves
      .filter((p) => p && p !== '#' && (current === p || current.startsWith(`${p}/`)))
      .sort((a, b) => b.length - a.length)[0] || '';
  }, [location.pathname, menuTree]);

  // ---- sidebar resizing (drag the handle on the sidebar's right edge) ----
  function startResize(e) {
    if (effCollapsed || isMobile) return;
    e.preventDefault();
    e.currentTarget.setPointerCapture(e.pointerId);
    setResizing(true);
  }

  function onResize(e) {
    if (!resizing) return;
    setSbWidth(clampWidth(e.clientX)); // the sidebar starts at x = 0, so the pointer position is its width
  }

  function endResize(e) {
    if (!resizing) return;
    try { e.currentTarget.releasePointerCapture(e.pointerId); } catch { /* already released */ }
    setResizing(false);
  }

  function onResizeKey(e) {
    if (e.key === 'ArrowLeft') { e.preventDefault(); setSbWidth((w) => clampWidth(w - 16)); }
    if (e.key === 'ArrowRight') { e.preventDefault(); setSbWidth((w) => clampWidth(w + 16)); }
    if (e.key === 'Home') { e.preventDefault(); setSbWidth(SB_DEFAULT); }
  }

  const shellClass = ['hs-shell', effCollapsed ? 'is-collapsed' : '', mobileOpen ? 'is-open' : '', resizing ? 'is-resizing' : '']
    .filter(Boolean)
    .join(' ');

  return (
    <div className={shellClass} style={{ '--sb-w': `${effCollapsed ? SB_COLLAPSED : sbWidth}px` }}>
      <header className="hs-topbar">
        <button
          type="button"
          className="hs-hamburger"
          onClick={() => setMobileOpen((o) => !o)}
          aria-label="Toggle menu"
          aria-expanded={mobileOpen}
        >
          <Menu size={22} />
        </button>

        <div className="hs-brand">
          <span className="hs-brand-badge">HR</span>
          <div className="hs-brand-text">
            <span className="hs-brand-short">HRMS</span>
            <span className="hs-brand-full">Human Resource Management System</span>
          </div>
        </div>

        <div className="hs-topbar-right">
          {languages.length > 1 && (
            <label className="hs-lang-wrap" title="Language">
              <Globe size={15} className="hs-lang-icon" />
              <select className="hs-lang" value={langCode} onChange={(e) => handleLanguageChange(e.target.value)} aria-label="Language">
                {languages.map((l) => <option key={l.code} value={l.code}>{l.name}</option>)}
              </select>
            </label>
          )}
        </div>
      </header>

      {mobileOpen && <div className="hs-backdrop" onClick={() => setMobileOpen(false)} aria-hidden="true" />}

      <aside className={`hs-sidebar ${mobileOpen ? 'open' : ''}`}>
        <div className="hs-sidebar-inner">
          <div className="hs-drawer-head">
            <span>Menu</span>
            <button type="button" className="hs-drawer-close" onClick={() => setMobileOpen(false)} aria-label="Close menu">
              <X size={18} />
            </button>
          </div>

          <AccountSwitcher collapsed={effCollapsed} />

          <nav className="hs-nav" aria-label="Main">
            <ul className="hs-menu-list">
              {menuTree.map((node) => (
                <MenuItem
                  key={node.id}
                  node={node}
                  depth={0}
                  collapsed={effCollapsed}
                  activePath={activePath}
                  onNavigate={() => setMobileOpen(false)}
                  onExpand={() => setCollapsed(false)}
                />
              ))}
            </ul>
          </nav>

          <div className="hs-sidebar-foot">
            <button type="button" className="hs-logout" onClick={handleLogout} title={effCollapsed ? t('logout') : undefined}>
              <LogOut size={18} />
              {!effCollapsed && <span>{t('logout')}</span>}
            </button>
          </div>
        </div>

        {!isMobile && (
          <>
            {!effCollapsed && (
              <div
                className="hs-resizer"
                role="separator"
                aria-orientation="vertical"
                aria-label="Resize sidebar"
                aria-valuemin={SB_MIN}
                aria-valuemax={SB_MAX}
                aria-valuenow={sbWidth}
                tabIndex={0}
                title="Drag to resize (double-click to reset)"
                onPointerDown={startResize}
                onPointerMove={onResize}
                onPointerUp={endResize}
                onPointerCancel={endResize}
                onDoubleClick={() => setSbWidth(SB_DEFAULT)}
                onKeyDown={onResizeKey}
              />
            )}

            <button
              type="button"
              className="hs-edge-toggle"
              onClick={() => setCollapsed((c) => !c)}
              aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
              title={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
            >
              {collapsed ? <ChevronRight size={15} /> : <ChevronLeft size={15} />}
            </button>
          </>
        )}
      </aside>

      <main className="hs-content">
        <AccessProvider
          access={access}
        >
          {children}
        </AccessProvider>
      </main>
    </div>
  );
}