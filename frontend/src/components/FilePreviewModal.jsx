import { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { getFileLink, downloadFile } from '../api/files';

const extOf = (n) => (n?.split('.').pop() || '').toLowerCase();
const kindOf = (n) => {
  const e = extOf(n);
  if (['jpg', 'jpeg', 'png', 'gif', 'webp'].includes(e)) return 'image';
  if (e === 'pdf') return 'pdf';
  return 'other';
};

// file = { fileId, type, url, storedFileName, originalFileName }
// getLink(fileId, download) is optional. Pages can pass their own link source, for example the "my profile"
// page uses an endpoint that checks the file really belongs to the logged-in employee.
export default function FilePreviewModal({ file, onClose, getLink }) {
  const [url, setUrl] = useState(null);
  const [error, setError] = useState(null);

  const name = file.originalFileName || file.storedFileName;
  const kind = kindOf(name);

  async function handleDownload() {
    if (!getLink) { downloadFile(file); return; }
    try {
      const { url: dl } = await getLink(file.fileId, true);
      const a = document.createElement('a');
      a.href = dl;
      a.rel = 'noopener';
      document.body.appendChild(a);
      a.click();
      a.remove();
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Download failed.');
    }
  }

  // Public files already have a permanent url; secure ones get a fresh signed link each time the modal opens.
  useEffect(() => {
    let cancelled = false;
    setUrl(null);
    setError(null);

    if (file.type === 'public') {
      setUrl(file.url);
      return undefined;
    }

    const request = getLink ? getLink(file.fileId, false) : getFileLink(file.fileId);
    request
      .then((d) => !cancelled && setUrl(d.url))
      .catch((err) => !cancelled && setError(err?.response?.data?.message || err?.message || 'Unable to load file.'));

    return () => { cancelled = true; };
  }, [file]); // eslint-disable-line react-hooks/exhaustive-deps

  // Esc closes, and the page behind doesn't scroll while the modal is open
  useEffect(() => {
    const onKey = (e) => e.key === 'Escape' && onClose();
    document.addEventListener('keydown', onKey);
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.removeEventListener('keydown', onKey);
      document.body.style.overflow = prev;
    };
  }, [onClose]);

  let body;
  if (error) {
    body = <div className="text-danger text-center p-5">{error}</div>;
  } else if (!url) {
    body = (
      <div className="text-center p-5">
        <div className="spinner-border text-primary" role="status" />
      </div>
    );
  } else if (kind === 'image') {
    body = (
      <div className="text-center p-2">
        <img src={url} alt={name} style={{ maxWidth: '100%', maxHeight: '100%', objectFit: 'contain' }} />
      </div>
    );
  } else if (kind === 'pdf') {
    body = <iframe src={url} title={name} style={{ width: '100%', height: '100%', border: 0 }} />;
  } else {
    body = (
      <div className="text-center text-muted p-5">
        <i className="bi bi-file-earmark fs-1 d-block mb-2" />
        Preview isn't available for this file type.
        <div className="mt-3">
          <button className="btn btn-primary btn-sm" onClick={handleDownload}>
            <i className="bi bi-download me-1" />Download
          </button>
        </div>
      </div>
    );
  }

  return createPortal(
    <>
      <div className="modal-backdrop show" style={{ zIndex: 2000 }} />
      <div className="modal d-block" tabIndex={-1} style={{ zIndex: 2001 }} onClick={onClose}>
        <div className="modal-dialog modal-xl modal-dialog-centered" onClick={(e) => e.stopPropagation()}>
          <div className="modal-content" style={{ height: '85vh' }}>
            <div className="modal-header py-2">
              <h6 className="modal-title text-truncate me-3" title={name}>{name}</h6>
              <div className="ms-auto d-flex gap-2">
                <button
                  className="btn btn-sm btn-outline-primary"
                  disabled={!url}
                  onClick={() => window.open(url, '_blank', 'noopener')}
                  title="Open in new tab"
                >
                  <i className="bi bi-box-arrow-up-right" />
                </button>
                <button className="btn btn-sm btn-outline-secondary" onClick={handleDownload} title="Download">
                  <i className="bi bi-download" />
                </button>
                <button className="btn btn-sm btn-outline-dark" onClick={onClose} title="Close">
                  <i className="bi bi-x-lg" />
                </button>
              </div>
            </div>
            <div className="modal-body p-0 bg-light d-flex align-items-center justify-content-center" style={{ overflow: 'auto' }}>
              {body}
            </div>
          </div>
        </div>
      </div>
    </>,
    document.body
  );
}

// For any page that wants preview without FileManager (e.g. a table row's "View" button):
//   const { previewFile, previewModal } = useFilePreview();
//   <button onClick={() => previewFile(doc)}>View</button>
//   {previewModal}
export function useFilePreview() {
  const [file, setFile] = useState(null);
  const previewModal = file ? <FilePreviewModal file={file} onClose={() => setFile(null)} /> : null;
  return { previewFile: setFile, previewModal };
}