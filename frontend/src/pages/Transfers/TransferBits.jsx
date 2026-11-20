export const errMsg = (e) => e?.response?.data?.message || e?.message || 'Something went wrong.';

export const fmtDate = (v) => {
  if (!v) return '—';
  const s = String(v).slice(0, 10);
  const d = new Date(`${s}T00:00:00`);
  return Number.isNaN(d.getTime()) ? s : d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
};

export const fmtDateTime = (v) => {
  if (!v) return '—';
  const d = new Date(v);
  return Number.isNaN(d.getTime()) ? String(v) : d.toLocaleString('en-GB', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
};

const STATUS = {
  PENDING: ['warning', 'In approval'], RETURNED: ['info', 'Returned'], VERIFIED: ['info', 'Verified'], APPROVED: ['primary', 'Approved'], ORDERED: ['primary', 'Order issued'],
  RELIEVED: ['info', 'Relieved'], COMPLETED: ['success', 'Completed'], REJECTED: ['danger', 'Rejected'], WITHDRAWN: ['secondary', 'Withdrawn'],
  DRAFT: ['secondary', 'Draft'], PUBLISHED: ['success', 'Open'], RANKED: ['info', 'Ranked'], LIST_PUBLISHED: ['primary', 'List published'], CLOSED: ['dark', 'Closed']
};

export function StatusBadge({ status }) {
  const [tone, label] = STATUS[status] || ['secondary', status];
  return <span className={`badge text-bg-${tone}`}>{label}</span>;
}

function checkView(c) {
  if (c.state === 'PASS') return ['success', 'Pass'];
  if (c.state === 'SKIP') return ['secondary', 'Not checked'];
  if (c.strength === 'BLOCK') return ['danger', 'Blocks the request'];
  if (c.strength === 'RELAX') return ['warning', 'Needs relaxation'];
  return ['warning', 'Warning'];
}

export function CheckList({ checks }) {
  if (!checks || checks.length === 0) return <div className="text-muted small">No rules apply to this transfer.</div>;
  return (
    <ul className="list-group list-group-flush">
      {checks.map((c, i) => {
        const [tone, label] = checkView(c);
        return (
          <li key={`${c.ruleKey}-${c.subject}-${i}`} className="list-group-item px-0 d-flex gap-2 align-items-start">
            <span className={`badge text-bg-${tone} flex-shrink-0`} style={{ minWidth: 112 }}>{label}</span>
            <div><div className="fw-semibold small">{c.name}</div><div className="small text-muted">{c.message}</div></div>
          </li>
        );
      })}
    </ul>
  );
}