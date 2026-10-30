import { useEffect, useState } from 'react';
import { Routes, Route, Navigate } from 'react-router-dom';
import api from './api/axios';

import FirstDeploySetup from './pages/FirstDeploySetup.jsx';
import Login from './pages/Login.jsx';
import Layout from './components/Layout.jsx';
import Dashboard from './pages/Dashboard.jsx';
import ProtectedRoute from './components/ProtectedRoute.jsx';
import RequireOrgSetup from './components/RequireOrgSetup.jsx';
import MenuBuilder from './pages/superadmin/MenuBuilder.jsx';
import EmployeeCreate from './pages/Employee/EmployeeCreate.jsx';
import LanguageSettings from './pages/superadmin/LanguageSettings.jsx';
import MenuAccess from './pages/superadmin/MenuAccess.jsx';
import OrganizationDetails from './pages/superadmin/OrganizationDetails.jsx';
import NodeTypes from './pages/superadmin/NodeTypes.jsx';
import OrganizationHierarchy from './pages/superadmin/OrganizationHierarchy.jsx';
import PlaceholderPage from './pages/PlaceholderPage.jsx';
import Department from './pages/superadmin/Department.jsx';
import AccessTest from './pages/superadmin/AccessTest.jsx';
import Designation from './pages/superadmin/Designation.jsx';
import Roles from './pages/superadmin/Roles.jsx';
import OrganizationDataFlowHierarchy from './pages/superadmin/OrganizationDataFlowHierarchy.jsx';
import ApiRateLimit from './pages/superadmin/ApiRateLimit.jsx';
import FileUpload from './pages/Fileupload.jsx';
import EmployeeCreate1 from './pages/Employee/EmployeeCreate1.jsx';
import EmployeeMasterSetup from './pages/Employee/EmployeeMasterSetup.jsx';
import EmployeeList from './pages/Employee/EmployeeList.jsx';
import MyProfile from './pages/Employee/MyProfile.jsx';

import RateLimitTest from './pages/RateLimitTest.jsx';

export default function App() {
  const [status, setStatus] = useState(null); // { firstDeployDone, secondLanguageCode, organizationSetupDone }
  const [checking, setChecking] = useState(true);

  useEffect(() => {
    api
      .get('/system/status')
      .then((res) => setStatus(res.data))
      .finally(() => setChecking(false));
  }, []);

  if (checking) return <div className="page-loading">Loading HRMS...</div>;

  // First deploy: force the language-selection screen before anything else
  if (status && !status.firstDeployDone) {
    return (
      <Routes><Route path="*" element={<FirstDeploySetup onDone={() => setStatus({ ...status, firstDeployDone: true })} />} /></Routes>
    );
  }

  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route path="/rate-limit-test" element={<RateLimitTest />} />
      <Route path="/" element={<ProtectedRoute><RequireOrgSetup organizationSetupDone={status?.organizationSetupDone} onOrgSetupDone={() => setStatus({ ...status, organizationSetupDone: true })}><Layout /></RequireOrgSetup></ProtectedRoute>}>
        <Route index element={<Navigate to="/dashboard" replace />} />
        <Route path="dashboard" element={<Dashboard />} />
        <Route path="admin/menus" element={<ProtectedRoute requiredRoles={['SUPERADMIN']}><MenuBuilder /></ProtectedRoute>} />
        <Route path="admin/employees/new" element={<ProtectedRoute><EmployeeCreate /></ProtectedRoute>} />
        <Route path="admin/language" element={<ProtectedRoute requiredRoles={['SUPERADMIN']}><LanguageSettings /></ProtectedRoute>} />
        <Route path="admin/menu-access" element={<ProtectedRoute requiredRoles={['SUPERADMIN']}><MenuAccess /></ProtectedRoute>} />
        <Route path="admin/api-rate-limit" element={<ProtectedRoute requiredRoles={['SUPERADMIN']}><ApiRateLimit /></ProtectedRoute>} />

        <Route path="admin/organization" element={<ProtectedRoute><OrganizationDetails /></ProtectedRoute>} />
        <Route path="admin/organization/node-types" element={<ProtectedRoute><NodeTypes /></ProtectedRoute>} />
        <Route path="admin/organization/hierarchy" element={<ProtectedRoute><OrganizationHierarchy /></ProtectedRoute>} />
        <Route path="admin/department" element={<ProtectedRoute><Department /></ProtectedRoute>} />
        <Route path="admin/designation" element={<ProtectedRoute><Designation /></ProtectedRoute>} />
        <Route path="admin/roles" element={<ProtectedRoute><Roles /></ProtectedRoute>} />
        <Route path="admin/organization/data-flow" element={<ProtectedRoute><OrganizationDataFlowHierarchy /></ProtectedRoute>} />
        <Route path="admin/employee-master-setup" element={<ProtectedRoute><EmployeeMasterSetup /></ProtectedRoute>} />
        <Route path="employee/add" element={<ProtectedRoute><EmployeeCreate1 /></ProtectedRoute>} />
        <Route path="employee/All" element={<ProtectedRoute><EmployeeList /></ProtectedRoute>} />
        <Route path="employee/:id/edit" element={<ProtectedRoute><EmployeeCreate1 /></ProtectedRoute>} />
        <Route path="employee/my-profile" element={<ProtectedRoute><MyProfile /></ProtectedRoute>} />
        <Route path="admin/access-test" element={<AccessTest />} />
        <Route path="file-upload" element={<FileUpload />} />
        <Route path="*" element={<PlaceholderPage />} />
      </Route>
    </Routes>
  );
}
