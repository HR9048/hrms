import { createContext, useContext, useEffect, useState } from 'react';
import api from '../api/axios';

const AuthContext = createContext(null);

export function AuthProvider({ children }) {
  const [account, setAccount] = useState(null); // { accountId, username, roleCode, roleName, employeeId, employeeName }
  const [siblingAccounts, setSiblingAccounts] = useState([]); // other accounts for the same employee
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const token = localStorage.getItem('hrms_token');
    if (!token) {
      setLoading(false);
      return;
    }
    api
      .get('/auth/me')
      .then((res) => {
        setAccount(res.data.account);
        setSiblingAccounts(res.data.accounts);
      })
      .catch(() => {
        localStorage.removeItem('hrms_token');
      })
      .finally(() => setLoading(false));
  }, []);

  async function login(username, password) {
    const res = await api.post('/auth/login', { username, password });
    localStorage.setItem('hrms_token', res.data.token);
    setAccount(res.data.account);
    setSiblingAccounts(res.data.accounts);
    return res.data;
  }

  async function switchAccount(targetAccountId) {
    const res = await api.post('/auth/switch-account', { targetAccountId });
    localStorage.setItem('hrms_token', res.data.token);
    setAccount(res.data.account);
    return res.data.account;
  }

  function logout() {
    localStorage.removeItem('hrms_token');
    setAccount(null);
    setSiblingAccounts([]);
  }

  return (
    <AuthContext.Provider value={{ account, siblingAccounts, loading, login, switchAccount, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  return useContext(AuthContext);
}
