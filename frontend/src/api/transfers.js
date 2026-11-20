import api from './axios';

const b = '/transfers';
const d = (r) => r.data;

// policy setup (super admin)
export const listPolicies = () => api.get(`${b}/policies`).then(d);
export const getPolicy = (id) => api.get(`${b}/policies/${id}`).then(d);
export const createPolicy = (body) => api.post(`${b}/policies`, body).then(d);
export const updatePolicy = (id, body) => api.put(`${b}/policies/${id}`, body);
export const deletePolicy = (id) => api.delete(`${b}/policies/${id}`);
export const clonePolicy = (id) => api.post(`${b}/policies/${id}/clone`).then(d);
export const finalizePolicy = (id) => api.post(`${b}/policies/${id}/finalize`);
export const savePolicyPart = (id, part, rows) => api.put(`${b}/policies/${id}/${part}`, rows); // types | categories | rules | steps | rotation | fieldmap

// windows
export const getWindowOptions = () => api.get(`${b}/windows/options`).then(d);
export const listWindows = () => api.get(`${b}/windows`).then(d);
export const getWindow = (id) => api.get(`${b}/windows/${id}`).then(d);
export const saveWindow = (id, body) => (id ? api.put(`${b}/windows/${id}`, body) : api.post(`${b}/windows`, body)).then(d);
export const deleteWindow = (id) => api.delete(`${b}/windows/${id}`);
export const windowAction = (id, action) => api.post(`${b}/windows/${id}/${action}`).then(d); // publish | rank | publish-list | finalize
export const resolveObjection = (oid, resolution) => api.post(`${b}/windows/objections/${oid}/resolve`, { resolution }).then(d);

// vacancy
export const getVacancy = (nodeId) => api.get(`${b}/vacancy`, { params: { nodeId } }).then(d);
export const saveVacancy = (body) => api.put(`${b}/vacancy`, body).then(d);
export const deleteVacancy = (nodeId, designationId) => api.delete(`${b}/vacancy`, { params: { nodeId, designationId } });

// requests
export const getTransferForm = () => api.get(`${b}/form`).then(d);
export const searchPeople = (search) => api.get(`${b}/people`, { params: { search } }).then(d);
export const checkTransfer = (body) => api.post(`${b}/check`, body).then(d);
export const submitTransfer = (body) => api.post(b, body).then(d);
export const listTransfers = (params) => api.get(b, { params }).then(d);
export const getTransfer = (id) => api.get(`${b}/${id}`).then(d);
export const transferAction = (id, action, body = {}) => api.post(`${b}/${id}/${action}`, body).then(d); // decide | consent | withdraw | resubmit | order | relieve | join | objection
export const bulkOrder = (body) => api.post(`${b}/bulk-order`, body).then(d);
export const getTransferFileLink = (id, fileId, download = false) => api.get(`${b}/${id}/files/${fileId}/link`, { params: { download } }).then(d);

// rotation due list
export const getRotation = () => api.get(`${b}/rotation`).then(d);
export const rotationTransfer = (body) => api.post(`${b}/rotation/transfer`, body).then(d);

// notifications
export const getNotifications = () => api.get(`${b}/notifications`).then(d);
export const getNotificationCount = () => api.get(`${b}/notifications/count`).then(d);
export const markNotificationsRead = (ids) => api.post(`${b}/notifications/read`, { ids: ids || [] }).then(d);