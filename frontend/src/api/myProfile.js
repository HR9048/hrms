import api from './axios';

export const getMyProfile = () => api.get('/employee-master/me').then((r) => r.data);

// signed link to one of the caller's own documents (the server checks that the file belongs to them)
export const getMyFileLink = (fileId, download = false) =>
  api.get(`/employee-master/me/files/${fileId}/link`, { params: { download } }).then((r) => r.data);