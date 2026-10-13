import { useRef, useState } from 'react';
import Swal from 'sweetalert2';
import { uploadFile, viewFile, downloadFile, deleteFile } from '../api/files';
import { useFilePreview } from './FilePreviewModal';

const isImage = (n) => /\.(jpe?g|png|gif|webp)$/i.test(n || '');
const errMsg = (err) => err?.response?.data?.message || err?.message || 'Something went wrong.';

/**
 * viewMode         "both" (default) | "modal" | "tab"
 *                  modal = preview on the same page, tab = open in a new browser tab
 * category         on-disk subfolder on the file server, e.g. "employee-docs"
 * defaultType      "secure" | "public"
 * allowTypeChoice  show the Public/Private toggle
 * accept           input accept attribute, e.g. ".pdf,.jpg,.png"
 * maxSizeMB        client-side check only; the real limit is on the server
 * multiple         allow more than one file
 * readOnly         hide the uploader (view/download only)
 * canDelete        show the remove button
 * initialFiles     [{ fileId, storedFileName, originalFileName, type, url }] already-saved files
 * onChange(files)  full list whenever it changes
 * onUploaded(file) after each successful upload
 */
export default function FileManager({
  viewMode = 'both',
  category,
  defaultType = 'secure',
  allowTypeChoice = false,
  accept,
  maxSizeMB = 20,
  multiple = true,
  readOnly = false,
  canDelete = true,
  initialFiles = [],
  onChange,
  onUploaded
}) {
  const inputRef = useRef(null);
  const filesRef = useRef(initialFiles);
  const [files, setFiles] = useState(initialFiles);
  const [type, setType] = useState(defaultType);
  const [uploading, setUploading] = useState(false);
  const [progress, setProgress] = useState(0);
  const [dragOver, setDragOver] = useState(false);
  const [busy, setBusy] = useState(null); // "<fileId>:<action>"
  const { previewFile, previewModal } = useFilePreview();

  const showModal = viewMode === 'modal' || viewMode === 'both';
  const showTab = viewMode === 'tab' || viewMode === 'both';

  function update(fn) {
    const next = fn(filesRef.current);
    filesRef.current = next;
    setFiles(next);
    onChange?.(next);
  }

  async function handleFiles(fileList) {
    const selected = Array.from(fileList || []);
    const toUpload = multiple ? selected : selected.slice(0, 1);

    for (const file of toUpload) {
      if (file.size > maxSizeMB * 1024 * 1024) {
        Swal.fire('File Too Large', `"${file.name}" is larger than ${maxSizeMB} MB.`, 'warning');
        continue;
      }

      setUploading(true);
      setProgress(0);
      try {
        const result = await uploadFile(file, { type, category, onProgress: setProgress });
        update((prev) => (multiple ? [result, ...prev] : [result]));
        onUploaded?.(result);
      } catch (err) {
        Swal.fire('Upload Failed', errMsg(err), 'error');
      } finally {
        setUploading(false);
      }
    }

    if (inputRef.current) inputRef.current.value = '';
  }

  async function run(file, action, fn) {
    setBusy(`${file.fileId}:${action}`);
    try {
      await fn();
    } catch (err) {
      Swal.fire(action === 'tab' ? 'Unable to Open File' : action === 'download' ? 'Download Failed' : 'Delete Failed', errMsg(err), 'error');
    } finally {
      setBusy(null);
    }
  }

  // viewFile() calls window.open synchronously, so nothing may be awaited before it
  const handleTab = (file) => run(file, 'tab', () => viewFile(file));
  const handleDownload = (file) => run(file, 'download', () => downloadFile(file));

  async function handleRemove(file) {
    const ok = await Swal.fire({
      title: 'Remove this file?',
      text: file.originalFileName || file.storedFileName,
      icon: 'warning',
      showCancelButton: true,
      confirmButtonText: 'Remove'
    });
    if (!ok.isConfirmed) return;

    run(file, 'delete', async () => {
      await deleteFile(file.type, file.fileId);
      update((prev) => prev.filter((f) => f.fileId !== file.fileId));
    });
  }

  const isBusy = (f, a) => busy === `${f.fileId}:${a}`;
  const spinner = <span className="spinner-border spinner-border-sm" role="status" />;

  return (
    <div>
      {!readOnly && allowTypeChoice && (
        <div className="btn-group btn-group-sm mb-2" role="group">
          <input type="radio" className="btn-check" id="fm-type-public" checked={type === 'public'} onChange={() => setType('public')} />
          <label className="btn btn-outline-success" htmlFor="fm-type-public"><i className="bi bi-unlock me-1" />Public</label>
          <input type="radio" className="btn-check" id="fm-type-secure" checked={type === 'secure'} onChange={() => setType('secure')} />
          <label className="btn btn-outline-danger" htmlFor="fm-type-secure"><i className="bi bi-lock me-1" />Private</label>
        </div>
      )}

      {!readOnly && (
        <div
          className={`border rounded-3 text-center p-4 ${dragOver ? 'border-primary bg-primary-subtle' : 'border-secondary-subtle bg-light'}`}
          style={{ cursor: uploading ? 'not-allowed' : 'pointer', transition: 'background-color .15s' }}
          onClick={() => !uploading && inputRef.current?.click()}
          onDragOver={(e) => { e.preventDefault(); if (!uploading) setDragOver(true); }}
          onDragLeave={() => setDragOver(false)}
          onDrop={(e) => {
            e.preventDefault();
            setDragOver(false);
            if (!uploading) handleFiles(e.dataTransfer.files);
          }}
        >
          <input
            ref={inputRef}
            type="file"
            accept={accept}
            multiple={multiple}
            className="d-none"
            disabled={uploading}
            onChange={(e) => handleFiles(e.target.files)}
          />
          {uploading ? (
            <>
              <div className="spinner-border spinner-border-sm text-primary mb-2" role="status" />
              <div className="small text-muted">Uploading... {progress}%</div>
              <div className="progress mt-2" style={{ height: 6 }}>
                <div className="progress-bar" role="progressbar" style={{ width: `${progress}%` }} />
              </div>
            </>
          ) : (
            <>
              <i className="bi bi-cloud-arrow-up fs-2 text-secondary d-block mb-2" />
              <div className="small text-muted">Click to choose {multiple ? 'file(s)' : 'a file'}, or drag {multiple ? 'them' : 'it'} here</div>
              {accept && <div className="small text-muted mt-1">Accepted: {accept}</div>}
            </>
          )}
        </div>
      )}

      {files.length > 0 && (
        <ul className="list-group list-group-flush mt-2">
          {files.map((f) => (
            <li key={f.fileId} className="list-group-item px-0 d-flex align-items-center gap-2">
              {f.type === 'public' && isImage(f.storedFileName) ? (
                <img src={f.url} alt="" className="rounded" style={{ width: 32, height: 32, objectFit: 'cover' }} />
              ) : (
                <i className={`bi ${f.type === 'public' ? 'bi-file-earmark' : 'bi-file-earmark-lock'} fs-5 text-secondary`} />
              )}

              <span className="flex-grow-1 text-truncate small" title={f.originalFileName}>
                {f.originalFileName || f.storedFileName}
              </span>

              <span className={`badge ${f.type === 'public' ? 'text-bg-success' : 'text-bg-secondary'}`}>
                {f.type === 'public' ? 'Public' : 'Private'}
              </span>

              {showModal && (
                <button type="button" className="btn btn-sm btn-outline-primary" disabled={!!busy} onClick={() => previewFile(f)} title="Preview here">
                  <i className="bi bi-eye" />
                </button>
              )}
              {showTab && (
                <button type="button" className="btn btn-sm btn-outline-primary" disabled={!!busy} onClick={() => handleTab(f)} title="Open in new tab">
                  {isBusy(f, 'tab') ? spinner : <i className="bi bi-box-arrow-up-right" />}
                </button>
              )}
              <button type="button" className="btn btn-sm btn-outline-secondary" disabled={!!busy} onClick={() => handleDownload(f)} title="Download">
                {isBusy(f, 'download') ? spinner : <i className="bi bi-download" />}
              </button>
              {!readOnly && canDelete && (
                <button type="button" className="btn btn-sm btn-outline-danger" disabled={!!busy} onClick={() => handleRemove(f)} title="Remove">
                  {isBusy(f, 'delete') ? spinner : <i className="bi bi-trash" />}
                </button>
              )}
            </li>
          ))}
        </ul>
      )}

      {previewModal}
    </div>
  );
}


/*/ Preview in modal only
<FileManager viewMode="modal" category="employee-docs" onChange={setDocs} />

// New tab only
<FileManager viewMode="tab" />

// Both buttons (the default, so you can omit the prop)
<FileManager viewMode="both" />

// Saved files, view/download only
<FileManager readOnly viewMode="modal" initialFiles={savedDocs} />*/