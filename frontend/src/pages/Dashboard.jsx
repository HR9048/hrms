import { useAuth } from '../context/AuthContext';
import { useLanguage } from '../context/LanguageContext';

export default function Dashboard() {
  const { account } = useAuth();
  const { t } = useLanguage();
const token = localStorage.getItem("hrms_token");

console.log("HRMS JWT Token:", token);
  return (
    <div className="page">
      <div className="page-header">
        <h1>{t('dashboard')}</h1>
        <p className="muted">
          {t('welcome')}, {account?.employeeName}
        </p>
      </div>

      <div className="card">
        <h3>{t('currentAccount')}</h3>
        <p className="muted">
          {account?.roleName} · {account?.username}
        </p>
      </div>

      <div className="stat-grid">
        <div className="stat-card">
          <div className="stat-label">Role</div>
          <div className="stat-value">{account?.roleName}</div>
        </div>
        <div className="stat-card">
          <div className="stat-label">Employee code</div>
          <div className="stat-value">{account?.employeeCode}</div>
        </div>
        <div className="stat-card">
          <div className="stat-label">Username</div>
          <div className="stat-value">{account?.username}</div>
        </div>
      </div>
    </div>
  );
}
