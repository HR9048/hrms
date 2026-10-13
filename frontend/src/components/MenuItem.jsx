import { useState } from 'react';
import { Link } from 'react-router-dom';
import * as LucideIcons from 'lucide-react';
import { Circle, ChevronDown, ChevronRight } from 'lucide-react';

export default function MenuItem({ node, depth = 0, collapsed = false, onNavigate }) {
  const [open, setOpen] = useState(false);
  const hasChildren = node.children && node.children.length > 0;

  const IconComponent = (node.icon && LucideIcons[node.icon]) || Circle;

  if (!hasChildren) {
    return (
      <li className={`menu-item depth-${depth}`}>
        <Link
          to={node.path || '#'}
          className="menu-link"
          title={collapsed ? node.label : undefined}
          onClick={onNavigate}
        >
          <IconComponent size={18} className="menu-icon" />
          {!collapsed && <span className="menu-label">{node.label}</span>}
        </Link>
      </li>
    );
  }

  return (
    <li className={`menu-item has-children depth-${depth} ${open ? 'open' : ''}`}>
      <button
        type="button"
        className="menu-link menu-toggle"
        onClick={() => setOpen((o) => !o)}
        title={collapsed ? node.label : undefined}
      >
        <IconComponent size={18} className="menu-icon" />
        {!collapsed && <span className="menu-label">{node.label}</span>}
        {!collapsed && (open ? <ChevronDown size={14} /> : <ChevronRight size={14} />)}
      </button>
      {open && !collapsed && (
        <ul className={`submenu submenu-depth-${depth}`}>
          {node.children.map((child) => (
            <MenuItem
              key={child.id}
              node={child}
              depth={depth + 1}
              collapsed={collapsed}
              onNavigate={onNavigate}
            />
          ))}
        </ul>
      )}
    </li>
  );
}