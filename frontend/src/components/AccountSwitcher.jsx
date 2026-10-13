import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useLanguage } from '../context/LanguageContext';

// Only rendered when the logged-in employee has MORE THAN ONE account
// (e.g. same person has both an Employee account and an HR account).
export default function AccountSwitcher() {
  const { account, siblingAccounts, switchAccount } = useAuth();
  const { t } = useLanguage();
  const [open, setOpen] = useState(false);
  const navigate = useNavigate();

  if (!siblingAccounts || siblingAccounts.length <= 1) return null;

  async function handleSwitch(accId) {
    if (accId === account.accountId) {
      setOpen(false);
      return;
    }
    await switchAccount(accId);
    setOpen(false);
    navigate('/dashboard');
  }

  return (
    <div className="account-switcher" onMouseEnter={() => setOpen(true)} onMouseLeave={() => setOpen(false)}>
      <button className="btn btn-ghost">{t('switchAccount')}: {account.roleName}</button>
      {open && (
        <ul className="account-switcher-list">
          {siblingAccounts.map((acc) => (
            <li
              key={acc.id}
              className={acc.id === account.accountId ? 'active' : ''}
              onClick={() => handleSwitch(acc.id)}
            >
              {acc.role_name}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
