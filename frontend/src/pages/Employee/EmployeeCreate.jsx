import { useEffect, useState } from 'react';
import api from '../../api/axios';
import { useLanguage } from '../../context/LanguageContext';

export default function EmployeeCreate() {
  const { t } = useLanguage();
  const [roles, setRoles] = useState([]);
  const [departments, setDepartments] = useState([]);
  const [designations, setDesignations] = useState([]);
  const [message, setMessage] = useState('');

  const [employee, setEmployee] = useState({
    employeeCode: '', firstName: '', lastName: '', gender: '', dob: '',
    email: '', phone: '', departmentId: '', designationId: '', joinDate: ''
  });

  // An employee may hold more than one account/role (Employee + HR + Payroll...)
  const [accounts, setAccounts] = useState([{ roleId: '', username: '', password: '' }]);

  useEffect(() => {
    api.get('/admin/roles').then((res) => setRoles(res.data));
    api.get('/admin/departments').then((res) => setDepartments(res.data));
    api.get('/admin/designations').then((res) => setDesignations(res.data));
  }, []);

  function updateAccount(index, field, value) {
    setAccounts((prev) => prev.map((a, i) => (i === index ? { ...a, [field]: value } : a)));
  }

  function addAccountRow() {
    setAccounts((prev) => [...prev, { roleId: '', username: '', password: '' }]);
  }

  function removeAccountRow(index) {
    setAccounts((prev) => prev.filter((_, i) => i !== index));
  }

  async function handleSubmit(e) {
    e.preventDefault();
    setMessage('');
    try {
      await api.post('/employees', { ...employee, accounts });
      setMessage('Employee created successfully. They can now log in with any of the accounts created.');
      setEmployee({
        employeeCode: '', firstName: '', lastName: '', gender: '', dob: '',
        email: '', phone: '', departmentId: '', designationId: '', joinDate: ''
      });
      setAccounts([{ roleId: '', username: '', password: '' }]);
    } catch (err) {
      setMessage(err.response?.data?.message || 'Something went wrong.');
    }
  }

  return (
    <div className="page">
      <h1>{t('createEmployee')}</h1>

      <form className="card" onSubmit={handleSubmit}>
        <h3>Employee master</h3>
        <div className="form-grid">
          <div>
            <label>Employee code</label>
            <input required value={employee.employeeCode} onChange={(e) => setEmployee({ ...employee, employeeCode: e.target.value })} />
          </div>
          <div>
            <label>First name</label>
            <input required value={employee.firstName} onChange={(e) => setEmployee({ ...employee, firstName: e.target.value })} />
          </div>
          <div>
            <label>Last name</label>
            <input value={employee.lastName} onChange={(e) => setEmployee({ ...employee, lastName: e.target.value })} />
          </div>
          <div>
            <label>Gender</label>
            <select value={employee.gender} onChange={(e) => setEmployee({ ...employee, gender: e.target.value })}>
              <option value="">—</option>
              <option value="M">Male</option>
              <option value="F">Female</option>
              <option value="O">Other</option>
            </select>
          </div>
          <div>
            <label>Date of birth</label>
            <input type="date" value={employee.dob} onChange={(e) => setEmployee({ ...employee, dob: e.target.value })} />
          </div>
          <div>
            <label>Email</label>
            <input type="email" value={employee.email} onChange={(e) => setEmployee({ ...employee, email: e.target.value })} />
          </div>
          <div>
            <label>Phone</label>
            <input value={employee.phone} onChange={(e) => setEmployee({ ...employee, phone: e.target.value })} />
          </div>
          <div>
            <label>Department</label>
            <select value={employee.departmentId} onChange={(e) => setEmployee({ ...employee, departmentId: e.target.value })}>
              <option value="">—</option>
              {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
            </select>
          </div>
          <div>
            <label>Designation</label>
            <select value={employee.designationId} onChange={(e) => setEmployee({ ...employee, designationId: e.target.value })}>
              <option value="">—</option>
              {designations.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
            </select>
          </div>
          <div>
            <label>Join date</label>
            <input type="date" value={employee.joinDate} onChange={(e) => setEmployee({ ...employee, joinDate: e.target.value })} />
          </div>
        </div>

        <h3>Login account(s)</h3>
        <p className="muted">
          Add one account per role. If this person is both an Employee and, say, HR, add two rows —
          they'll be able to toggle between them after logging in.
        </p>

        {accounts.map((acc, i) => (
          <div className="account-row" key={i}>
            <select required value={acc.roleId} onChange={(e) => updateAccount(i, 'roleId', e.target.value)}>
              <option value="">Role...</option>
              {roles.map((r) => <option key={r.id} value={r.id}>{r.role_name}</option>)}
            </select>
            <input required placeholder="Username" value={acc.username} onChange={(e) => updateAccount(i, 'username', e.target.value)} />
            <input required type="password" placeholder="Password" value={acc.password} onChange={(e) => updateAccount(i, 'password', e.target.value)} />
            {accounts.length > 1 && (
              <button type="button" className="btn btn-small btn-danger" onClick={() => removeAccountRow(i)}>Remove</button>
            )}
          </div>
        ))}

        <button type="button" className="btn btn-ghost" onClick={addAccountRow}>+ Add another account/role</button>

        {message && <p className="muted">{message}</p>}

        <div className="form-actions">
          <button type="submit" className="btn btn-primary">{t('save')}</button>
        </div>
      </form>
    </div>
  );
}
