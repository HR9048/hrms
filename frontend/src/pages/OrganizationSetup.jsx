import { useState } from 'react';
import api from '../api/axios';
import { useLanguage } from '../context/LanguageContext';

// Shown exactly once, right after the existing first-deploy/language setup,
// when system_settings.organization_setup_done = 0. Same visual language as
// FirstDeploySetup.jsx (auth-screen / setup-card) so the two feel like one flow.
export default function OrganizationSetup({ onDone }) {
  const { t } = useLanguage();

  const [form, setForm] = useState({
    organizationName: '', organizationCode: '', logo: '', address: '', contactNumber: '', email: ''
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  function update(field, value) {
    setForm((f) => ({ ...f, [field]: value }));
  }

  async function handleSubmit(e) {
    e.preventDefault();
    setError('');

    if (!form.organizationName.trim() || !form.organizationCode.trim()) {
      setError('Organization name and code are required.');
      return;
    }

    setSaving(true);
    try {
      await api.post('/organization', form);
      onDone();
    } catch (err) {
      setError(err.response?.data?.message || 'Something went wrong');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="auth-screen">
      <div className="auth-card setup-card">
        <div className="brand-badge">HR</div>

        <h1>{t('organizationSetupTitle')}</h1>
        <p className="muted">{t('organizationSetupSubtitle')}</p>

        <form onSubmit={handleSubmit}>
          <div className="field">
            <label>{t('organizationName')} *</label>
            <input
              value={form.organizationName}
              onChange={(e) => update('organizationName', e.target.value)}
              required
              autoFocus
            />
          </div>

          <div className="field">
            <label>{t('organizationCode')} *</label>
            <input
              value={form.organizationCode}
              onChange={(e) => update('organizationCode', e.target.value)}
              placeholder="KSRTC"
              required
            />
          </div>

          <div className="field">
            <label>Logo URL</label>
            <input value={form.logo} onChange={(e) => update('logo', e.target.value)} placeholder="https://..." />
          </div>

          <div className="field">
            <label>Address</label>
            <input value={form.address} onChange={(e) => update('address', e.target.value)} />
          </div>

          <div className="field">
            <label>Contact number</label>
            <input value={form.contactNumber} onChange={(e) => update('contactNumber', e.target.value)} />
          </div>

          <div className="field">
            <label>Email</label>
            <input type="email" value={form.email} onChange={(e) => update('email', e.target.value)} />
          </div>

          {error && <p className="error-text">{error}</p>}

          <button className="btn btn-primary full-width mt-16" type="submit" disabled={saving}>
            {saving ? '...' : t('continue')}
          </button>
        </form>
      </div>
    </div>
  );
}
