import api from './axios';

const base = '/employee-master';

export const getMaster = () => api.get(base).then((r) => r.data);
export const getLookups = () => api.get(`${base}/lookups`).then((r) => r.data);

export const createSection = (body) => api.post(`${base}/sections`, body).then((r) => r.data);
export const updateSection = (id, body) => api.put(`${base}/sections/${id}`, body);
export const deleteSection = (id) => api.delete(`${base}/sections/${id}`);
export const finalizeSection = (id) => api.post(`${base}/sections/${id}/finalize`);

export const createField = (body) => api.post(`${base}/fields`, body).then((r) => r.data);
export const updateField = (id, body) => api.put(`${base}/fields/${id}`, body).then((r) => r.data);
export const deleteField = (id) => api.delete(`${base}/fields/${id}`);
export const reorderFields = (sectionId, ids) => api.put(`${base}/sections/${sectionId}/fields/order`, { ids });
export const saveSettings = (body) => api.put(`${base}/settings`, body);

export const listEmployees = (params) => api.get(`${base}/employees`, { params }).then((r) => r.data);
export const exportEmployees = (params) => api.get(`${base}/employees/export`, { params, responseType: 'blob' }).then((r) => r.data);
export const getEmployee = (id) => api.get(`${base}/employees/${id}`).then((r) => r.data);
export const getEmployeeHistory = (id) => api.get(`${base}/employees/${id}/history`).then((r) => r.data);
export const createEmployee = (body) => api.post(`${base}/employees`, body).then((r) => r.data);
export const updateEmployee = (id, body) => api.put(`${base}/employees/${id}`, body).then((r) => r.data);

export const getEmployeeAccounts = (id) => api.get(`${base}/employees/${id}/accounts`).then((r) => r.data);
export const addEmployeeAccount = (id, body) => api.post(`${base}/employees/${id}/accounts`, body).then((r) => r.data);