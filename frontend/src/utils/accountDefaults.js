export const DEFAULT_PASSWORD = '123456';
export const MIN_PASSWORD = 6;
export const EMPLOYEE_ROLE_CODE = 'EMPLOYEE'; // roles.role_code of the plain employee role

export const isEmployeeRole = (role) => String(role?.code || '').toUpperCase() === EMPLOYEE_ROLE_CODE;