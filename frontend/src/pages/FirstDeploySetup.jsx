import { useEffect, useState } from 'react';
import api from '../api/axios';
import { useLanguage } from '../context/LanguageContext';

// English and Hindi are always available.
// On first deploy, Super Admin selects exactly ONE additional language.
// This can only be changed later by a Super Admin.
export default function FirstDeploySetup({ onDone }) {
  const { setLangCode, t } = useLanguage();
  const [languages, setLanguages] = useState([]); // selectable languages excluding English & Hindi
  const [selected, setSelected] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    api.get('/languages/all').then((res) => {
      // English and Hindi are always on, so don't show them
      // as selectable additional languages.
      const options = res.data.filter(
        (l) => l.code !== 'en' && l.code !== 'hi'
      );

      setLanguages(options);

      if (options.length) {
        setSelected(options[0].code);
      }
    });
  }, []);

  async function handleContinue() {
    if (!selected) return;

    setSaving(true);
    setError('');

    try {
      await api.post('/system/first-deploy-language', {
        languageCode: selected,
      });

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

        <h1>{t('firstDeployTitle')}</h1>

        <p className="muted">
          English and Hindi are always available. Choose one more language
          for your organization — a Super Admin can change this later from
          Settings.
        </p>

        <div className="lang-tile-grid">

          {/* English - Always On */}
          <div className="lang-tile fixed">
            <span className="lang-tile-name">English</span>
            <span className="lang-tile-tag">Always on</span>
          </div>

          {/* Hindi - Always On */}
          <div className="lang-tile fixed">
            <span className="lang-tile-name">हिन्दी</span>
            <span className="lang-tile-tag">Always on</span>
          </div>

          {/* Additional language options */}
          {languages.map((lang) => (
            <button
              key={lang.code}
              type="button"
              className={`lang-tile ${
                selected === lang.code ? 'selected' : ''
              }`}
              onClick={() => setSelected(lang.code)}
            >
              <span className="lang-tile-name">{lang.name}</span>
            </button>
          ))}
        </div>

        {error && <p className="error-text">{error}</p>}

        <button
          className="btn btn-primary full-width"
          onClick={handleContinue}
          disabled={saving || !selected}
        >
          {saving ? '...' : t('continue')}
        </button>
      </div>
    </div>
  );
}
