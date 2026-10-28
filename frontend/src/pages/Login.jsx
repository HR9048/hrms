import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Building2, Eye, EyeOff, Globe, Lock, ShieldCheck, User, Users } from 'lucide-react';
import api from '../api/axios';
import { useAuth } from '../context/AuthContext';
import { useLanguage } from '../context/LanguageContext';
//import './Login.css';

const FEATURES = [
  { icon: Users, title: 'Employee records', text: 'Profiles, documents and logins in one place.' },
  { icon: Building2, title: 'Organization structure', text: 'Units, workplaces and data-flow visibility.' },
  { icon: ShieldCheck, title: 'Role-based access', text: 'Every user sees only what they are allowed to.' }
];

export default function Login() {
  const { login } = useAuth();
  const { langCode, setLangCode, t } = useLanguage();
  const navigate = useNavigate();

  const [languages, setLanguages] = useState([]);
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [capsOn, setCapsOn] = useState(false);
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    api.get('/languages').then((res) => setLanguages(res.data));
  }, []);

  function onChangeLanguage(code) {
    localStorage.setItem('hrms_lang_manual', '1');
    setLangCode(code);
  }

  async function handleSubmit(e) {
    e.preventDefault();
    setError('');
    setSubmitting(true);
    try {
      await login(username.trim(), password);
      navigate('/dashboard');
    } catch (err) {
      setError(err.response?.data?.message || t('invalidCredentials'));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="lg-page">
      {/* brand panel (shrinks to a header band on small screens) */}
      <aside className="lg-brand">
        <div className="lg-brand-top">
          <span className="lg-logo">HR</span>
          <span className="lg-brand-name">HRMS</span>
        </div>

        <div className="lg-hero">
          <h1>Human Resource<br />Management System</h1>
          <p>Manage your people, organization and access from one secure place.</p>
        </div>

        <ul className="lg-features">
          {FEATURES.map(({ icon: Icon, title, text }) => (
            <li key={title}>
              <span className="lg-feature-icon"><Icon size={18} /></span>
              <span>
                <strong>{title}</strong>
                <small>{text}</small>
              </span>
            </li>
          ))}
        </ul>
      </aside>

      <main className="lg-panel">
        <div className="lg-panel-top">
          {languages.length > 1 && (
            <label className="lg-lang" title="Language">
              <Globe size={15} />
              <select value={langCode} onChange={(e) => onChangeLanguage(e.target.value)} aria-label="Language">
                {languages.map((l) => <option key={l.code} value={l.code}>{l.name}</option>)}
              </select>
            </label>
          )}
        </div>

        <div className="lg-card-wrap">
          <div className="lg-card">
            <div className="lg-card-head">
              <h2>{t('login')}</h2>
              <p>{t('appName')}</p>
            </div>

            <form onSubmit={handleSubmit} noValidate>
              <div className="lg-field">
                <label htmlFor="lg-username">{t('username')}</label>
                <div className="lg-input-wrap">
                  <User size={17} className="lg-input-icon" />
                  <input
                    id="lg-username"
                    className="lg-input"
                    value={username}
                    onChange={(e) => setUsername(e.target.value)}
                    autoComplete="username"
                    autoFocus
                    required
                  />
                </div>
              </div>

              <div className="lg-field">
                <label htmlFor="lg-password">{t('password')}</label>
                <div className="lg-input-wrap">
                  <Lock size={17} className="lg-input-icon" />
                  <input
                    id="lg-password"
                    className="lg-input"
                    type={showPassword ? 'text' : 'password'}
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    onKeyUp={(e) => setCapsOn(e.getModifierState && e.getModifierState('CapsLock'))}
                    onBlur={() => setCapsOn(false)}
                    autoComplete="current-password"
                    required
                  />
                  <button
                    type="button"
                    className="lg-eye"
                    onClick={() => setShowPassword((s) => !s)}
                    aria-label={showPassword ? 'Hide password' : 'Show password'}
                    tabIndex={-1}
                  >
                    {showPassword ? <EyeOff size={17} /> : <Eye size={17} />}
                  </button>
                </div>
                {capsOn && <div className="lg-hint">Caps Lock is on</div>}
              </div>

              {error && <div className="lg-error" role="alert">{error}</div>}

              <button className="lg-submit" type="submit" disabled={submitting || !username.trim() || !password}>
                {submitting ? <span className="lg-spinner" aria-hidden="true" /> : null}
                {submitting ? 'Signing in…' : t('login')}
              </button>
            </form>
          </div>
        </div>

        <footer className="lg-foot">© {new Date().getFullYear()} HRMS</footer>
      </main>
    </div>
  );
}