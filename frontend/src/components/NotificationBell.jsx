import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Bell } from 'lucide-react';
import { getNotificationCount, getNotifications, markNotificationsRead } from '../api/transfers';

const ago = (v) => {
  const s = Math.max(1, Math.round((Date.now() - new Date(v).getTime()) / 1000));
  if (s < 60) return 'just now';
  if (s < 3600) return `${Math.floor(s / 60)} min ago`;
  if (s < 86400) return `${Math.floor(s / 3600)} h ago`;
  return new Date(v).toLocaleDateString('en-GB', { day: '2-digit', month: 'short' });
};

export default function NotificationBell() {
  const navigate = useNavigate();
  const [count, setCount] = useState(0);
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState([]);
  const box = useRef(null);

  useEffect(() => {
    let stop = false;
    const tick = () => getNotificationCount().then((d) => !stop && setCount(d.unread)).catch(() => {});
    tick();
    const t = setInterval(tick, 60000);
    return () => { stop = true; clearInterval(t); };
  }, []);

  useEffect(() => {
    if (!open) return undefined;
    const onDown = (e) => { if (!box.current?.contains(e.target)) setOpen(false); };
    const onKey = (e) => { if (e.key === 'Escape') setOpen(false); };
    document.addEventListener('mousedown', onDown);
    document.addEventListener('touchstart', onDown);
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('mousedown', onDown); document.removeEventListener('touchstart', onDown); document.removeEventListener('keydown', onKey); };
  }, [open]);

  async function toggle() {
    const next = !open;
    setOpen(next);
    if (next) {
      try { const d = await getNotifications(); setItems(d.items); setCount(d.unread); } catch { /* keep the old list */ }
    }
  }

  async function go(n) {
    setOpen(false);
    if (!n.isRead) { markNotificationsRead([n.id]).then(() => setCount((x) => Math.max(0, x - 1))).catch(() => {}); }
    if (n.link) navigate(n.link);
  }

  async function readAll() {
    await markNotificationsRead(null).catch(() => {});
    setItems((p) => p.map((n) => ({ ...n, isRead: 1 })));
    setCount(0);
  }

  return (
    <div ref={box} style={{ position: 'relative' }}>
      <button
        type="button"
        onClick={toggle}
        aria-label="Notifications"
        style={{ position: 'relative', display: 'inline-flex', alignItems: 'center', justifyContent: 'center', padding: 7, border: 0, borderRadius: 10, background: 'rgba(255,255,255,.1)', color: '#fff', cursor: 'pointer' }}
      >
        <Bell size={20} />
        {count > 0 && (
          <span style={{ position: 'absolute', top: -4, right: -4, minWidth: 18, height: 18, padding: '0 4px', borderRadius: 9, background: '#ef4444', color: '#fff', fontSize: 11, fontWeight: 700, display: 'grid', placeItems: 'center' }}>
            {count > 99 ? '99+' : count}
          </span>
        )}
      </button>

      {open && (
        <div style={{ position: 'absolute', right: 0, top: 46, width: 'min(380px, 92vw)', maxHeight: '70vh', overflowY: 'auto', background: '#fff', color: '#0f172a', borderRadius: 14, border: '1px solid #e2e8f0', boxShadow: '0 20px 45px rgba(15,23,42,.25)', zIndex: 400 }}>
          <div className="d-flex align-items-center px-3 py-2 border-bottom">
            <b className="small">Notifications</b>
            {count > 0 && <button type="button" className="btn btn-link btn-sm ms-auto p-0" onClick={readAll}>Mark all read</button>}
          </div>
          {items.length === 0 && <div className="text-center text-muted small py-4">Nothing yet.</div>}
          {items.map((n) => (
            <button
              type="button"
              key={n.id}
              onClick={() => go(n)}
              className="d-block w-100 text-start border-0 border-bottom px-3 py-2"
              style={{ background: n.isRead ? '#fff' : '#eef2ff', cursor: 'pointer' }}
            >
              <div className="small fw-semibold">{n.title}</div>
              {n.body && <div className="small text-muted">{n.body}</div>}
              <div className="text-muted" style={{ fontSize: 11 }}>{ago(n.createdAt)}</div>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}