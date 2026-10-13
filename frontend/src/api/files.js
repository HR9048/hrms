import api from './axios';

export const uploadFile = (file, { type = 'secure', category, onProgress } = {}) => {
  const fd = new FormData();
  fd.append('file', file);
  return api
    .post('/files/upload', fd, {
      params: { type, category },
      onUploadProgress: (e) => e.total && onProgress?.(Math.round((e.loaded / e.total) * 100))
    })
    .then((r) => r.data);
};

export const getFileLink = (fileId, { download = false, name } = {}) =>
  api.get(`/files/${fileId}/link`, { params: { download, name } }).then((r) => r.data);

export const deleteFile = (type, fileId) => api.delete(`/files/${type}/${fileId}`);

// file = { fileId, type, url, originalFileName }  (the shape the upload endpoint returns)

// The tab must be opened synchronously inside the click handler or popup blockers kill it,
// so there must be no `await` before window.open.
export async function viewFile(file) {
  if (file.type === 'public') {
    window.open(file.url, '_blank', 'noopener');
    return;
  }
  const tab = window.open('', '_blank');
  if (!tab) throw new Error('Your browser blocked the new tab. Allow popups for this site and try again.');
  try {
    const { url } = await getFileLink(file.fileId);
    tab.location.href = url;
  } catch (err) {
    tab.close();
    throw err;
  }
}

export async function downloadFile(file) {
  let url;
  if (file.type === 'public') {
    url = `${file.url}?download=true&name=${encodeURIComponent(file.originalFileName || '')}`;
  } else {
    ({ url } = await getFileLink(file.fileId, { download: true, name: file.originalFileName || file.storedFileName }));
  }
  const a = document.createElement('a');
  a.href = url;
  a.rel = 'noopener';
  document.body.appendChild(a);
  a.click(); // server sends Content-Disposition: attachment, so the page stays put
  a.remove();
}