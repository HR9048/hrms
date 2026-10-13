import { Navigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';

// requiredRoles is optional: e.g. <ProtectedRoute requiredRoles={['SUPERADMIN']}>
export default function ProtectedRoute({ children, requiredRoles }) {
  const { account, loading } = useAuth();

  if (loading) return <div className="page-loading">Loading...</div>;
  if (!account) return <Navigate to="/login" replace />;

  if (requiredRoles && !requiredRoles.includes(account.roleCode)) {
    return <Navigate to="/dashboard" replace />;
  }

  return children;
}
