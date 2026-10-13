import api from './axios';

// { scope: { unrestricted, homeNodeId }, organizations, nodeTypes, nodes } limited to what the logged-in user may see
export const getOrgScope = () => api.get('/data-scope').then((r) => r.data);