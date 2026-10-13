import { useAuth } from '../context/AuthContext';
import OrganizationSetup from '../pages/OrganizationSetup.jsx';

// Sits INSIDE ProtectedRoute (so we already know the user is logged in) and
// OUTSIDE Layout (so nothing in the sidebar/menu tree renders until the
// organization exists). Mirrors how App.jsx already blocks everything with
// FirstDeploySetup before firstDeployDone — this is the same idea, one gate
// later, and only for a logged-in Super Admin.
export default function RequireOrgSetup({ organizationSetupDone, onOrgSetupDone, children }) {
  const { account } = useAuth();

  if (organizationSetupDone) {
    return children;
  }

  if (account?.roleCode === 'SUPERADMIN') {
    return <OrganizationSetup onDone={onOrgSetupDone} />;
  }

  // Anyone else logging in before the Super Admin has finished setup
  // (shouldn't normally happen, but don't let them hit a broken app).
  return (
    <div className="auth-screen">
      <div className="auth-card setup-card">
        <div className="brand-badge">HR</div>
        <h1>Setup in progress</h1>
        <p className="muted">
          Your organization hasn't been set up yet. Please contact your Super Admin.
        </p>
      </div>
    </div>
  );
}
