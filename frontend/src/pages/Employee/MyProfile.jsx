import { useEffect, useState } from 'react';
import Swal from 'sweetalert2';
import FilePreviewModal from '../../components/FilePreviewModal';
import { getMyProfile, getMyFileLink } from '../../api/myProfile';

const errMsg = (e) => e?.response?.data?.message || e?.message || 'Something went wrong.';

const initialsOf = (first, last) => `${(first || '')[0] || ''}${(last || '')[0] || ''}`.toUpperCase() || '?';

function fmtDate(v) {
  if (!v) return null;
  const d = new Date(`${v}T00:00:00`);
  return Number.isNaN(d.getTime()) ? v : d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
}

const showValue = (item) => (item.type === 'DATE' ? fmtDate(item.value) : item.value);

function fileIcon(name = '') {
  const ext = (name.split('.').pop() || '').toLowerCase();
  if (ext === 'pdf') return 'bi-file-earmark-pdf text-danger';
  if (['jpg', 'jpeg', 'png', 'gif', 'webp'].includes(ext)) return 'bi-file-earmark-image text-success';
  if (['doc', 'docx'].includes(ext)) return 'bi-file-earmark-word text-primary';
  if (['xls', 'xlsx', 'csv'].includes(ext)) return 'bi-file-earmark-excel text-success';
  return 'bi-file-earmark text-secondary';
}

function Info({ label, value }) {
  return (
    <div className="col-12 col-sm-6 col-lg-4">
      <div className="text-muted small">{label}</div>
      <div className="fw-semibold text-break">{value ?? <span className="text-muted fw-normal">—</span>}</div>
    </div>
  );
}

function SectionCard({ icon, title, children }) {
  return (
    <div className="card border-0 shadow-sm mb-3">
      <div className="card-header bg-white fw-semibold d-flex align-items-center gap-2">
        <i className={`bi ${icon} text-primary`} />
        {title}
      </div>
      <div className="card-body">{children}</div>
    </div>
  );
}

export default function MyProfile() {
  const [profile, setProfile] = useState(null);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(null); // "<fileId>:view" | "<fileId>:download"
  const [photoUrl, setPhotoUrl] = useState(null);
  const [preview, setPreview] = useState(null);

  useEffect(() => {
    getMyProfile().then(setProfile).catch((e) => setError(errMsg(e)));
  }, []);

  // Profile photo: only when the employee has one. The signed link is refreshed before it expires.
  // Without a photo (or if it can't be loaded) the initials circle is shown instead.
  const photoId = profile?.photo?.fileId;
  useEffect(() => {
    if (!photoId) { setPhotoUrl(null); return undefined; }

    let cancelled = false;
    let timer;
    async function load() {
      try {
        const d = await getMyFileLink(photoId);
        if (cancelled) return;
        setPhotoUrl(d.url);
        const ms = Math.max(5000, d.expiresAt * 1000 - Date.now() - 30000);
        timer = setTimeout(load, ms);
      } catch {
        if (!cancelled) setPhotoUrl(null);
      }
    }
    load();

    return () => { cancelled = true; clearTimeout(timer); };
  }, [photoId]);

  // the new tab has to be opened inside the click itself, or popup blockers stop it (common on phones)
  async function viewDoc(doc) {
    const tab = window.open('', '_blank');
    if (!tab) {
      Swal.fire('Popup blocked', 'Your browser blocked the new tab. Allow popups for this site and try again.', 'warning');
      return;
    }
    setBusy(`${doc.fileId}:view`);
    try {
      const { url } = await getMyFileLink(doc.fileId);
      tab.location.href = url;
    } catch (e) {
      tab.close();
      Swal.fire('Unable to open the document', errMsg(e), 'error');
    } finally {
      setBusy(null);
    }
  }

  async function downloadDoc(doc) {
    setBusy(`${doc.fileId}:download`);
    try {
      const { url } = await getMyFileLink(doc.fileId, true);
      const a = document.createElement('a');
      a.href = url;
      a.rel = 'noopener';
      document.body.appendChild(a);
      a.click(); // the server sends it as an attachment, so the page stays where it is
      a.remove();
    } catch (e) {
      Swal.fire('Download failed', errMsg(e), 'error');
    } finally {
      setBusy(null);
    }
  }

  if (error) {
    return (
      <div className="container py-4" style={{ maxWidth: 1000 }}>
        <div className="alert alert-danger mb-0">{error}</div>
      </div>
    );
  }

  if (!profile) {
    return <div className="p-4 text-center"><span className="spinner-border spinner-border-sm" /></div>;
  }

  const fullName = [profile.firstName, profile.lastName].filter(Boolean).join(' ');
  const org = profile.organization;
  const active = profile.status === 'ACTIVE';
  const avatarSize = { width: 88, height: 88, border: '4px solid #fff' };

  return (
    <div className="container-fluid py-3" style={{ maxWidth: 1000 }}>
      {/* header */}
      <div className="card border-0 shadow-sm mb-3 overflow-hidden">
        <div style={{ height: 76, background: 'linear-gradient(135deg, #312e81, #4f46e5)' }} />
        <div className="card-body pt-0 pb-3">
          <div className="d-flex flex-column flex-sm-row align-items-center align-items-sm-start gap-3">
            {/* only the avatar overlaps the banner; the name sits on the white part of the card */}
            <div className="flex-shrink-0" style={{ marginTop: -44 }}>
              {photoUrl ? (
                <button
                  type="button"
                  className="p-0 border-0 bg-transparent d-block"
                  style={{ cursor: 'zoom-in' }}
                  title="View photo"
                  aria-label="View profile photo"
                  onClick={() => setPreview({
                    fileId: profile.photo.fileId,
                    type: 'secure',
                    originalFileName: profile.photo.fileName,
                    storedFileName: profile.photo.fileName
                  })}
                >
                  <img
                    src={photoUrl}
                    alt={fullName}
                    onError={() => setPhotoUrl(null)}
                    className="rounded-circle shadow"
                    style={{ ...avatarSize, objectFit: 'cover', background: '#e2e8f0' }}
                  />
                </button>
              ) : (
                <div
                  className="rounded-circle d-flex align-items-center justify-content-center fw-bold text-white shadow"
                  style={{ ...avatarSize, fontSize: '1.9rem', background: 'linear-gradient(135deg, #818cf8, #4f46e5)' }}
                >
                  {initialsOf(profile.firstName, profile.lastName)}
                </div>
              )}
            </div>

            <div className="text-center text-sm-start flex-grow-1 pt-sm-3" style={{ minWidth: 0 }}>
              <h4 className="mb-1 text-break text-dark">{fullName}</h4>
              <div className="text-muted">{profile.employeeCode}</div>
              <div className="mt-2 d-flex flex-wrap justify-content-center justify-content-sm-start gap-2">
                <span className={`badge ${active ? 'text-bg-success' : 'text-bg-secondary'}`}>{active ? 'Active' : 'Inactive'}</span>
                {profile.roles.map((r) => <span key={r} className="badge text-bg-primary">{r}</span>)}
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* organization */}
      {org && (
        <SectionCard icon="bi-diagram-3" title="Organization">
          <div className="row g-3">
            <Info label="Organization" value={org.name} />
            {org.levels.map((l) => <Info key={`${l.type}-${l.name}`} label={l.type} value={l.name} />)}
            <Info label="Assigned since" value={fmtDate(org.since)} />
          </div>
        </SectionCard>
      )}

      {/* one card per section of the employee master */}
      {profile.sections.map((s) => (
        <SectionCard key={s.key} icon="bi-person-lines-fill" title={s.title}>
          <div className="row g-3">
            {s.items.map((it, i) => <Info key={`${it.label}-${i}`} label={it.label} value={showValue(it)} />)}
          </div>
        </SectionCard>
      ))}

      {/* documents */}
      <div className="card border-0 shadow-sm mb-3">
        <div className="card-header bg-white fw-semibold d-flex align-items-center gap-2">
          <i className="bi bi-folder2-open text-primary" />
          My documents
          <span className="badge text-bg-light ms-1">{profile.documents.length}</span>
        </div>

        {profile.documents.length === 0 ? (
          <div className="card-body text-center text-muted py-4">
            <i className="bi bi-file-earmark fs-2 d-block mb-1" />
            No documents have been uploaded for you yet.
          </div>
        ) : (
          <ul className="list-group list-group-flush">
            {profile.documents.map((d) => (
              <li key={d.fileId} className="list-group-item d-flex flex-column flex-sm-row align-items-sm-center gap-2 py-3">
                <div className="d-flex align-items-center gap-3 flex-grow-1" style={{ minWidth: 0 }}>
                  <i className={`bi ${fileIcon(d.fileName)} fs-2 flex-shrink-0`} />
                  <div style={{ minWidth: 0 }}>
                    <div className="fw-semibold">
                      {d.label} <span className="text-muted fw-normal small">· {d.section}</span>
                    </div>
                    <div className="small text-muted text-truncate">{d.fileName}</div>
                  </div>
                </div>
                {/* on click view setPreview and on download call downloadDoc */}
                <div className="d-flex gap-2 flex-shrink-0">
                  <button type="button" className="btn btn-sm btn-outline-primary" disabled={!!busy} onClick={() => setPreview({
                    fileId: d.fileId,
                    type: 'secure',
                    originalFileName: d.fileName,
                    storedFileName: d.fileName
                  })
                  }>
                    {busy === `${d.fileId}:view` ? <span className="spinner-border spinner-border-sm" /> : <i className="bi bi-eye me-1" />}View
                  </button>
                  <button type="button" className="btn btn-sm btn-outline-secondary" disabled={!!busy} onClick={() => downloadDoc(d)}>
                    {busy === `${d.fileId}:download` ? <span className="spinner-border spinner-border-sm" /> : <i className="bi bi-download me-1" />}Download
                  </button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>

      {/* same preview window as the documents module, fed by the "own files" endpoint */}
      {preview && (
        <FilePreviewModal
          file={preview}
          onClose={() => setPreview(null)}
          getLink={(fileId, download) => getMyFileLink(fileId, download)}
        />
      )}
    </div>
  );
}