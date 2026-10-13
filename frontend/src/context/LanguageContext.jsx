import { createContext, useContext, useEffect, useState } from 'react';
import en from '../i18n/en.json';
import hi from '../i18n/hi.json';
import kn from '../i18n/kn.json';
import mr from '../i18n/mr.json';

const DICTS = { en, hi, kn, mr };

const LanguageContext = createContext(null);

export function LanguageProvider({ children }) {
  const [langCode, setLangCode] = useState(localStorage.getItem('hrms_lang') || 'en');

  useEffect(() => {
    localStorage.setItem('hrms_lang', langCode);
  }, [langCode]);

  // t() looks up a static UI string; falls back to English then the key itself
  const t = (key) => DICTS[langCode]?.[key] || DICTS.en[key] || key;

  const value = { langCode, setLangCode, t };
  return <LanguageContext.Provider value={value}>{children}</LanguageContext.Provider>;
}

export function useLanguage() {
  return useContext(LanguageContext);
}
