import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useNavigate } from 'react-router-dom';
import { Check, ChevronDown, Repeat } from 'lucide-react';
import { useAuth } from '../context/AuthContext';
import { useLanguage } from '../context/LanguageContext';

const initials = (name = '') =>
  name.trim().split(/\s+/).slice(0, 2).map((w) => w[0]?.toUpperCase() || '').join('') || '?';

// The profile block at the top of the sidebar. When the employee has more than one account
// (e.g. an Employee account and an HR account) it becomes a button that opens a list to switch between them.
export default function AccountSwitcher({ collapsed = false }) {
  const { account, siblingAccounts, switchAccount } = useAuth();
  const { t } = useLanguage();
  const navigate = useNavigate();

  const btnRef = useRef(null);
  const popRef = useRef(null);
  const [open, setOpen] = useState(false);
  const [busyId, setBusyId] = useState(null);
  const [pos, setPos] = useState(null);

  const accounts = siblingAccounts || [];
  const canSwitch = accounts.length > 1;
  const name = account?.employeeName || account?.roleName || 'Account';

  // place the popup next to the button (to the right of the icon bar when collapsed, below the card otherwise)
  useLayoutEffect(() => {
    if (!open || !btnRef.current) return;
    const r = btnRef.current.getBoundingClientRect();
    const width = collapsed ? 240 : Math.max(r.width, 220);
    let left = collapsed ? r.right + 10 : r.left;
    let top = collapsed ? r.top : r.bottom + 8;
    left = Math.min(left, window.innerWidth - width - 8);
    top = Math.min(top, window.innerHeight - 140);
    setPos({ left: Math.max(8, left), top: Math.max(8, top), width });
  }, [open, collapsed]);

  // close on outside click / tap, Esc, or resize
  useEffect(() => {
    if (!open) return undefined;
    const onDown = (e) => {
      if (btnRef.current?.contains(e.target) || popRef.current?.contains(e.target)) return;
      setOpen(false);
    };
    const onKey = (e) => { if (e.key === 'Escape') setOpen(false); };
    const onResize = () => setOpen(false);
    document.addEventListener('mousedown', onDown);
    document.addEventListener('touchstart', onDown);
    document.addEventListener('keydown', onKey);
    window.addEventListener('resize', onResize);
    return () => {
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('touchstart', onDown);
      document.removeEventListener('keydown', onKey);
      window.removeEventListener('resize', onResize);
    };
  }, [open]);

  async function handleSwitch(accId) {
    if (accId === account.accountId) {
      setOpen(false);
      return;
    }
    setBusyId(accId);
    try {
      await switchAccount(accId);
      setOpen(false);
      navigate('/dashboard');
    } finally {
      setBusyId(null);
    }
  }

  const card = (
    <>
      <span className="hs-avatar">{initials(name)}</span>
      <span className="hs-profile-text">
        <span className="hs-profile-name">{name}</span>
        <span className="hs-profile-role">{account?.roleName}</span>
      </span>
      {canSwitch && <ChevronDown size={16} className="hs-profile-chev" />}
    </>
  );

  if (!canSwitch) {
    return <div className="hs-profile" title={collapsed ? `${name} · ${account?.roleName || ''}` : undefined}>{card}</div>;
  }

  return (
    <>
      <button
        ref={btnRef}
        type="button"
        className={`hs-profile ${open ? 'open' : ''}`}
        onClick={() => setOpen((o) => !o)}
        aria-haspopup="menu"
        aria-expanded={open}
        title={collapsed ? `${t('switchAccount')}: ${account?.roleName || ''}` : undefined}
      >
        {card}
      </button>

      {open && pos && createPortal(
        <div ref={popRef} className="hs-pop" role="menu" style={{ left: pos.left, top: pos.top, width: pos.width }}>
          <div className="hs-pop-title"><Repeat size={13} />{t('switchAccount')}</div>
          {accounts.map((acc) => {
            const active = acc.id === account.accountId;
            return (
              <button
                key={acc.id}
                type="button"
                role="menuitem"
                className={`hs-pop-item ${active ? 'active' : ''}`}
                disabled={busyId != null}
                onClick={() => handleSwitch(acc.id)}
              >
                <span className="hs-pop-avatar">{initials(acc.role_name)}</span>
                <span className="hs-pop-label">{acc.role_name}</span>
                {active && <Check size={16} />}
              </button>
            );
          })}
        </div>,
        document.body
      )}
    </>
  );
}