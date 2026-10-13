import { useEffect, useState } from 'react';
import api from '../../api/axios';
import { useLanguage } from '../../context/LanguageContext';

export default function LanguageSettings() {
const { t } = useLanguage();

const [languages, setLanguages] = useState([]);
const [optionalLanguages, setOptionalLanguages] = useState([]);
const [current, setCurrent] = useState(null);
const [selected, setSelected] = useState('');
const [message, setMessage] = useState('');

function load() {
api.get('/languages/all').then((res) => {
const allLanguages = res.data;


  // English and Hindi are always available
  const fixedLanguages = allLanguages.filter(
    (l) => l.code === 'en' || l.code === 'hi'
  );

  // All other languages are optional
  const otherLanguages = allLanguages.filter(
    (l) => l.code !== 'en' && l.code !== 'hi'
  );

  setLanguages(fixedLanguages);
  setOptionalLanguages(otherLanguages);

  // Active optional language
  const active = otherLanguages.find((l) => l.is_active);

  setCurrent(active || null);
  setSelected(active ? active.code : '');
});


}

useEffect(() => {
load();
}, []);

async function handleSave() {
setMessage('');


if (!selected) {
  setMessage('Please select an optional language.');
  return;
}

try {
  await api.put('/system/second-language', {
    languageCode: selected,
  });

  setMessage(
    'System language updated. Everyone will see it the next time they load the app.'
  );

  load();
} catch (err) {
  setMessage(
    err.response?.data?.message || 'Something went wrong.'
  );
}


}

return ( <div className="page"> <div className="page-header"> <h1>{t('languageSettings')}</h1>


    <p className="muted">
      English and Hindi are always available. You can optionally enable
      one additional language for everyone.
    </p>
  </div>

  <div className="card">

    {/* Always Available Languages */}
    <h3>Always Available</h3>

    <div className="lang-tile-grid">
      {languages.map((lang) => (
        <div
          key={lang.code}
          className="lang-tile fixed"
        >
          <span className="lang-tile-name">
            {lang.name}
          </span>

          <span className="lang-tile-tag">
            Always on
          </span>
        </div>
      ))}
    </div>

    {/* Optional Languages */}
    <h3 style={{ marginTop: '24px' }}>
      Optional Language
    </h3>

    <p className="muted">
      Select one additional language to make available to everyone.
    </p>

    <div className="lang-tile-grid">
      {optionalLanguages.map((lang) => (
        <button
          key={lang.code}
          type="button"
          className={`lang-tile ${
            selected === lang.code ? 'selected' : ''
          }`}
          onClick={() => setSelected(lang.code)}
        >
          <span className="lang-tile-name">
            {lang.name}
          </span>

          {current?.code === lang.code && (
            <span className="lang-tile-tag">
              Current
            </span>
          )}
        </button>
      ))}
    </div>

    {message && (
      <p className="muted">
        {message}
      </p>
    )}

    <div className="form-actions">
      <button
        className="btn btn-primary"
        onClick={handleSave}
        disabled={
          !selected ||
          selected === current?.code
        }
      >
        {t('save')}
      </button>
    </div>
  </div>
</div>


);
}
