import { nodeOptions, indent } from './DynamicField';

// Pass the lookups from useOrgScope().data (or from a page's own scoped lookups).
// <OrgNodeSelect lookups={data} value={node} onChange={setNode} />
export default function OrgNodeSelect({ lookups, value, onChange, placeholder = 'All organization nodes', className = 'form-select form-select-sm', disabled }) {
  const { organizations = [], nodes = [] } = lookups || {};

  const renderOptions = (list) => nodeOptions(list).map((n) => <option key={n.id} value={n.id}>{indent(n)}</option>);

  return (
    <select className={className} value={value ?? ''} disabled={disabled} onChange={(e) => onChange(e.target.value)}>
      <option value="">{placeholder}</option>
      {organizations.length > 1
        ? organizations.map((o) => (
            <optgroup key={o.id} label={o.name}>{renderOptions(nodes.filter((n) => n.orgId === o.id))}</optgroup>
          ))
        : renderOptions(nodes)}
    </select>
  );
}