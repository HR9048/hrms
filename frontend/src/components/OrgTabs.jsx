import { NavLink } from 'react-router-dom';

const tabs = [
  { to: '/admin/organization', label: 'Organization Details', icon: 'bi-building', end: true },
  { to: '/admin/organization/node-types', label: 'Node Types', icon: 'bi-layers' },
  { to: '/admin/organization/hierarchy', label: 'Hierarchy', icon: 'bi-diagram-3' }
];

export default function OrgTabs() {
  return (
    <ul className="nav nav-tabs">
      {tabs.map((tab) => (
        <li className="nav-item" key={tab.to}>
          <NavLink
            to={tab.to}
            end={tab.end}
            className={({ isActive }) => `nav-link ${isActive ? 'active fw-semibold' : ''}`}
          >
            <i className={`bi ${tab.icon} me-2`} />
            {tab.label}
          </NavLink>
        </li>
      ))}
    </ul>
  );
}