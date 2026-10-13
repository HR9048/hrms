import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import api from '../api/axios';
import { useAuth } from '../context/AuthContext';
import { useLanguage } from '../context/LanguageContext';

export default function Login() {
  const { login } = useAuth();
  const { langCode, setLangCode, t } = useLanguage();
  const navigate = useNavigate();

  const [languages, setLanguages] = useState([]);
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
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
      await login(username, password);
      navigate('/dashboard');
    } catch (err) {
      setError(err.response?.data?.message || t('invalidCredentials'));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="auth-screen">
      <div className="auth-card">
        <div className="login-top-row">
          <span className="brand-badge">HR</span>
          {languages.length > 1 && (
            <select className="select-pill" value={langCode} onChange={(e) => onChangeLanguage(e.target.value)}>
              {languages.map((l) => (
                <option key={l.code} value={l.code}>
                  {l.name}
                </option>
              ))}
            </select>
          )}
        </div>

        <h1>{t('appName')}</h1>
        <h2>{t('login')}</h2>

        <form onSubmit={handleSubmit}>
          <div className="field">
            <label>{t('username')}</label>
            <input value={username} onChange={(e) => setUsername(e.target.value)} required autoFocus />
          </div>

          <div className="field">
            <label>{t('password')}</label>
            <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
          </div>

          {error && <p className="error-text">{error}</p>}

          <button className="btn btn-primary full-width mt-16" type="submit" disabled={submitting}>
            {submitting ? '...' : t('login')}
          </button>
        </form>
      </div>
    </div>
  );
}
