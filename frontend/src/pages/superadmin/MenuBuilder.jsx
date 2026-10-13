import { useEffect, useRef, useState } from 'react';
import { ListTree, Plus, X } from 'lucide-react';
import api from '../../api/axios';
import { useLanguage } from '../../context/LanguageContext';

const emptyForm = { id: null, parentId: '', menuKey: '', routePath: '', icon: '', orderNo: 0, translations: {} };

export default function MenuBuilder() {
  const { t } = useLanguage();
  const [languages, setLanguages] = useState([]);
  const [menus, setMenus] = useState([]);
  const [form, setForm] = useState(emptyForm);
  const [message, setMessage] = useState('');
  const [showModal, setShowModal] = useState(false);

  const tableRef = useRef(null);
  const dtRef = useRef(null);

  function loadMenus() {
    api.get('/menus/admin/all').then((res) => setMenus(res.data));
  }

  useEffect(() => {
    api.get('/languages').then((res) => setLanguages(res.data));
    loadMenus();
  }, []);

  // init / destroy / reinit DataTable whenever the underlying data changes
  useEffect(() => {
    const $ = window.jQuery;
    if (!$ || !$.fn?.DataTable || !tableRef.current) return;

    if ($.fn.DataTable.isDataTable(tableRef.current)) {
      $(tableRef.current).DataTable().destroy();
    }

    dtRef.current = $(tableRef.current).DataTable({
      pageLength: 10,
      order: [],
      destroy: true
    });

    return () => {
      if ($.fn.DataTable.isDataTable(tableRef.current)) {
        $(tableRef.current).DataTable().destroy();
      }
    };
  }, [menus]);

  function updateTranslation(languageId, label) {
    setForm((f) => ({ ...f, translations: { ...f.translations, [languageId]: label } }));
  }

  function editMenu(m) {
    const translationsObj = {};
    (m.translations || '').split('|').forEach((pair) => {
      if (!pair) return;
      const [langId, label] = pair.split(':');
      translationsObj[langId] = label;
    });
    setForm({
      id: m.id,
      parentId: m.parent_id || '',
      menuKey: m.menu_key,
      routePath: m.route_path || '',
      icon: m.icon || '',
      orderNo: m.order_no,
      translations: translationsObj
    });
    setShowModal(true);
  }

  function openAddModal() {
    setForm(emptyForm);
    setMessage('');
    setShowModal(true);
  }

  function closeModal() {
    setShowModal(false);
    setForm(emptyForm);
    setMessage('');
  }

  async function handleSubmit(e) {
    e.preventDefault();
    setMessage('');
    const translations = languages
      .filter((l) => form.translations[l.id])
      .map((l) => ({ languageId: l.id, label: form.translations[l.id] }));

    if (translations.length === 0) {
      setMessage('Please provide a label in at least one language.');
      return;
    }

    const payload = {
      parentId: form.parentId || null,
      menuKey: form.menuKey,
      routePath: form.routePath || null,
      icon: form.icon || null,
      orderNo: Number(form.orderNo) || 0,
      translations
    };

    try {
      if (form.id) {
        await api.put(`/menus/${form.id}`, { ...payload, isActive: true });
        setMessage('Menu updated.');
      } else {
        await api.post('/menus', payload);
        setMessage('Menu created.');
      }
      setForm(emptyForm);
      loadMenus();
      setShowModal(false);
    } catch (err) {
      setMessage(err.response?.data?.message || 'Something went wrong.');
    }
  }

  async function handleDelete(id) {
    if (!window.confirm('Delete this menu and all its sub-menus?')) return;
    await api.delete(`/menus/${id}`);
    loadMenus();
  }

  function indentLabel(m) {
    const depth = m.parent_id ? (menus.find((p) => p.id === m.parent_id)?.parent_id ? 2 : 1) : 0;
    return `${'— '.repeat(depth)}${m.menu_key}`;
  }

  function parentLabel(m) {
    if (!m.parent_id) return <span className="muted">—</span>;
    const parent = menus.find((p) => p.id === m.parent_id);
    return parent ? parent.menu_key : <span className="muted">—</span>;
  }

  function translationsLabel(m) {
    const pairs = (m.translations || '').split('|').filter(Boolean);
    if (pairs.length === 0) return <span className="muted">—</span>;
    return pairs
      .map((pair) => {
        const [langId, label] = pair.split(':');
        const lang = languages.find((l) => String(l.id) === String(langId));
        return `${lang ? lang.name : langId}: ${label}`;
      })
      .join(', ');
  }

  return (
    <div className="page">
      <div className="page-toolbar">
        <div>
          <h1>{t('menuBuilder')}</h1>
          <p className="muted">Create the menus and sub-menus that appear in the sidebar, per language.</p>
        </div>
        <button type="button" className="btn btn-primary" onClick={openAddModal}>
          <Plus size={16} /> Add Menu
        </button>
      </div>

      <div className="card">
        <h3>Existing menus</h3>
        {menus.length === 0 ? (
          <div className="empty-state">
            <div className="empty-icon"><ListTree size={20} /></div>
            <p>No menus yet. Create your first one.</p>
          </div>
        ) : (
          <table ref={tableRef} className="simple-table datatable" style={{ width: '100%' }}>
            <thead>
              <tr>
                <th>Key</th>
                <th>Parent</th>
                <th>Path</th>
                <th>Icon</th>
                <th>Order</th>
                <th>Translations</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {menus.map((m) => (
                <tr key={m.id}>
                  <td>{indentLabel(m)}</td>
                  <td>{parentLabel(m)}</td>
                  <td>{m.route_path || <span className="muted">—</span>}</td>
                  <td>{m.icon || <span className="muted">—</span>}</td>
                  <td><span className="badge badge-neutral">{m.order_no}</span></td>
                  <td>{translationsLabel(m)}</td>
                  <td>
                    <button className="btn btn-small" onClick={() => editMenu(m)}>Edit</button>
                    <button className="btn btn-small btn-danger" onClick={() => handleDelete(m.id)}>Delete</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {showModal && (
        <div className="app-modal-overlay" onClick={closeModal}>
          <div className="app-modal" onClick={(e) => e.stopPropagation()}>
            <div className="app-modal-header">
              <h3>{form.id ? 'Edit menu item' : 'Create menu / sub-menu / sub-sub-menu'}</h3>
              <button type="button" className="btn-icon" onClick={closeModal}>
                <X size={18} />
              </button>
            </div>

            <form className="app-modal-body" onSubmit={handleSubmit}>
              <label>Parent (leave blank for a top-level menu)</label>
              <select value={form.parentId} onChange={(e) => setForm({ ...form, parentId: e.target.value })}>
                <option value="">— Top level —</option>
                {menus
                  .filter((m) => m.id !== form.id)
                  .map((m) => (
                    <option key={m.id} value={m.id}>
                      {indentLabel(m)}
                    </option>
                  ))}
              </select>

              <label>Menu key (unique, programmatic, e.g. employee.master)</label>
              <input value={form.menuKey} onChange={(e) => setForm({ ...form, menuKey: e.target.value })} required />

              <label>Route path (leave blank if this is just a grouping node)</label>
              <input value={form.routePath} onChange={(e) => setForm({ ...form, routePath: e.target.value })} placeholder="/employee/master" />

              <label>Icon name (optional, lucide-react icon)</label>
              <input value={form.icon} onChange={(e) => setForm({ ...form, icon: e.target.value })} placeholder="Users" />

              <label>Order</label>
              <input type="number" value={form.orderNo} onChange={(e) => setForm({ ...form, orderNo: e.target.value })} />

              <h4>Labels per language</h4>
              {languages.map((l) => (
                <div key={l.id}>
                  <label>{l.name}</label>
                  <input
                    value={form.translations[l.id] || ''}
                    onChange={(e) => updateTranslation(l.id, e.target.value)}
                  />
                </div>
              ))}

              {message && <p className="muted">{message}</p>}

              <div className="form-actions">
                <button type="submit" className="btn btn-primary">{t('save')}</button>
                <button type="button" className="btn btn-ghost" onClick={closeModal}>
                  {t('cancel')}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}