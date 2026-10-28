import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import * as LucideIcons from 'lucide-react';
import { Circle, ChevronRight } from 'lucide-react';

const norm = (p) => String(p || '').split('?')[0].split('#')[0].replace(/\/+$/, '').toLowerCase();

// is this item (or one of its descendants) the page the user is on?
const containsActive = (node, activePath) =>
  !!activePath &&
  (norm(node.path) === activePath || (node.children || []).some((c) => containsActive(c, activePath)));

export default function MenuItem({ node, depth = 0, collapsed = false, activePath = '', onNavigate, onExpand }) {
  const hasChildren = node.children && node.children.length > 0;
  const active = containsActive(node, activePath);
  const [open, setOpen] = useState(active);

  // keep the group open while one of its pages is the current page
  useEffect(() => {
    if (active) setOpen(true);
  }, [activePath]); // eslint-disable-line react-hooks/exhaustive-deps

  const IconComponent = (node.icon && LucideIcons[node.icon]) || Circle;

  if (!hasChildren) {
    return (
      <li className={`hs-item depth-${depth}`}>
        <Link
          to={node.path || '#'}
          className={`hs-link ${active ? 'active' : ''}`}
          title={collapsed ? node.label : undefined}
          aria-current={active ? 'page' : undefined}
          onClick={onNavigate}
        >
          <IconComponent size={18} className="hs-icon" />
          {!collapsed && <span className="hs-label">{node.label}</span>}
        </Link>
      </li>
    );
  }

  const expanded = open && !collapsed;

  function toggle() {
    if (collapsed) {
      // a group can't open inside the narrow icon bar: widen the sidebar and open it
      onExpand?.();
      setOpen(true);
      return;
    }
    setOpen((o) => !o);
  }

  return (
    <li className={`hs-item hs-group depth-${depth} ${expanded ? 'open' : ''} ${active ? 'has-active' : ''}`}>
      <button
        type="button"
        className="hs-link"
        onClick={toggle}
        title={collapsed ? node.label : undefined}
        aria-expanded={expanded}
      >
        <IconComponent size={18} className="hs-icon" />
        {!collapsed && (
          <>
            <span className="hs-label">{node.label}</span>
            <ChevronRight size={14} className="hs-chev" />
          </>
        )}
      </button>

      {expanded && (
        <ul className="hs-sub">
          {node.children.map((child) => (
            <MenuItem
              key={child.id}
              node={child}
              depth={depth + 1}
              collapsed={collapsed}
              activePath={activePath}
              onNavigate={onNavigate}
              onExpand={onExpand}
            />
          ))}
        </ul>
      )}
    </li>
  );
}