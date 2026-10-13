-- =========================================================
-- HRMS Database Schema
-- Engine: MySQL 8+
-- =========================================================

CREATE DATABASE IF NOT EXISTS hrms_db CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE hrms_db;

-- ---------------------------------------------------------
-- 1. LANGUAGES  (picked once at first deploy, editable later only by SuperAdmin)
-- ---------------------------------------------------------
CREATE TABLE languages (
  id          INT AUTO_INCREMENT PRIMARY KEY,
  code        VARCHAR(10)  NOT NULL UNIQUE,   -- en, hi, kn, mr
  name        VARCHAR(50)  NOT NULL,          -- English, Hindi, Kannada, Marathi
  is_active   TINYINT(1)   NOT NULL DEFAULT 1,
  is_default  TINYINT(1)   NOT NULL DEFAULT 0 -- only one row should be 1
);

-- Only English is active by default. Exactly ONE more language is switched
-- on at first-deploy time (or later by Super Admin) - see system_settings below.
INSERT INTO languages (code, name, is_active, is_default) VALUES
('en', 'English',  1, 1),
('hi', 'Hindi',    0, 0),
('kn', 'Kannada',  0, 0),
('mr', 'Marathi',  0, 0);

-- ---------------------------------------------------------
-- 2. SYSTEM SETTINGS  (first-deploy flag, default language, org name, etc.)
-- ---------------------------------------------------------
CREATE TABLE system_settings (
  id            INT AUTO_INCREMENT PRIMARY KEY,
  setting_key   VARCHAR(100) NOT NULL UNIQUE,
  setting_value VARCHAR(255) NOT NULL
);

INSERT INTO system_settings (setting_key, setting_value) VALUES
('first_deploy_done', '0'),      -- flips to 1 once the second language is chosen
('second_language_code', '');    -- e.g. 'kn' / 'hi' / 'mr' - set at first deploy, editable by Super Admin

-- ---------------------------------------------------------
-- 3. ROLES  (Employee may hold several -> multiple accounts)
-- ---------------------------------------------------------
CREATE TABLE roles (
  id          INT AUTO_INCREMENT PRIMARY KEY,
  role_code   VARCHAR(50)  NOT NULL UNIQUE,   -- SUPERADMIN, HR, PAYROLL, EMPLOYEE, MANAGER...
  role_name   VARCHAR(100) NOT NULL,
  description VARCHAR(255)
);

INSERT INTO roles (role_code, role_name, description) VALUES
('SUPERADMIN', 'Super Admin', 'Full system control incl. menu builder & language'),
('HR',         'HR',          'Human resources operations'),
('PAYROLL',    'Payroll',     'Payroll processing'),
('MANAGER',    'Manager',     'Team / department manager'),
('EMPLOYEE',   'Employee',    'Self-service employee account');

-- ---------------------------------------------------------
-- 4. ORG LOOKUPS
-- ---------------------------------------------------------
CREATE TABLE departments (
  id    INT AUTO_INCREMENT PRIMARY KEY,
  name  VARCHAR(100) NOT NULL
);

CREATE TABLE designations (
  id    INT AUTO_INCREMENT PRIMARY KEY,
  name  VARCHAR(100) NOT NULL
);

-- ---------------------------------------------------------
-- 5. EMPLOYEE MASTER  (one row per real human)
-- ---------------------------------------------------------
CREATE TABLE employees (
  id              INT AUTO_INCREMENT PRIMARY KEY,
  employee_code   VARCHAR(30)  NOT NULL UNIQUE,
  first_name      VARCHAR(100) NOT NULL,
  last_name       VARCHAR(100),
  gender          ENUM('M','F','O'),
  dob             DATE,
  email           VARCHAR(150) UNIQUE,
  phone           VARCHAR(20),
  department_id   INT,
  designation_id  INT,
  join_date       DATE,
  status          ENUM('ACTIVE','INACTIVE') NOT NULL DEFAULT 'ACTIVE',
  created_at      DATETIME DEFAULT CURRENT_TIMESTAMP,
  updated_at      DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  FOREIGN KEY (department_id)  REFERENCES departments(id),
  FOREIGN KEY (designation_id) REFERENCES designations(id)
);

-- ---------------------------------------------------------
-- 6. USER ACCOUNTS  (login credentials).
--    One employee -> many accounts, each tied to ONE role.
--    e.g. same person: HAR001-EMPLOYEE, HAR001-HR
-- ---------------------------------------------------------
CREATE TABLE user_accounts (
  id             INT AUTO_INCREMENT PRIMARY KEY,
  employee_id    INT NOT NULL,
  role_id        INT NOT NULL,
  username       VARCHAR(100) NOT NULL UNIQUE,
  password_hash  VARCHAR(255) NOT NULL,
  is_default     TINYINT(1) NOT NULL DEFAULT 0,  -- account shown first after login
  is_active      TINYINT(1) NOT NULL DEFAULT 1,
  created_at     DATETIME DEFAULT CURRENT_TIMESTAMP,
  FOREIGN KEY (employee_id) REFERENCES employees(id) ON DELETE CASCADE,
  FOREIGN KEY (role_id)     REFERENCES roles(id),
  UNIQUE KEY uq_employee_role (employee_id, role_id) -- one account per role per employee
);

-- ---------------------------------------------------------
-- 7. MENUS  (self-referencing => menu / sub-menu / sub-sub-menu, superadmin built)
-- ---------------------------------------------------------
CREATE TABLE menus (
  id          INT AUTO_INCREMENT PRIMARY KEY,
  parent_id   INT NULL,                 -- NULL = top-level menu
  menu_key    VARCHAR(100) NOT NULL,    -- unique programmatic key e.g. 'dashboard', 'employee.master'
  route_path  VARCHAR(150),             -- react route, NULL if it's just a grouping node
  icon        VARCHAR(50),              -- icon name (e.g. lucide-react icon)
  order_no    INT NOT NULL DEFAULT 0,
  is_active   TINYINT(1) NOT NULL DEFAULT 1,
  created_at  DATETIME DEFAULT CURRENT_TIMESTAMP,
  FOREIGN KEY (parent_id) REFERENCES menus(id) ON DELETE CASCADE
);

-- ---------------------------------------------------------
-- 8. MENU TRANSLATIONS  (label of every menu, per language)
-- ---------------------------------------------------------
CREATE TABLE menu_translations (
  id          INT AUTO_INCREMENT PRIMARY KEY,
  menu_id     INT NOT NULL,
  language_id INT NOT NULL,
  label       VARCHAR(150) NOT NULL,
  FOREIGN KEY (menu_id)     REFERENCES menus(id) ON DELETE CASCADE,
  FOREIGN KEY (language_id) REFERENCES languages(id) ON DELETE CASCADE,
  UNIQUE KEY uq_menu_lang (menu_id, language_id)
);

-- ---------------------------------------------------------
-- 9. ROLE <-> MENU PERMISSIONS  (superadmin assigns which role sees which menu)
-- ---------------------------------------------------------
CREATE TABLE role_menu_permissions (
  id         INT AUTO_INCREMENT PRIMARY KEY,
  role_id    INT NOT NULL,
  menu_id    INT NOT NULL,
  can_view   TINYINT(1) NOT NULL DEFAULT 1,
  can_add    TINYINT(1) NOT NULL DEFAULT 0,
  can_edit   TINYINT(1) NOT NULL DEFAULT 0,
  can_delete TINYINT(1) NOT NULL DEFAULT 0,
  FOREIGN KEY (role_id) REFERENCES roles(id) ON DELETE CASCADE,
  FOREIGN KEY (menu_id) REFERENCES menus(id) ON DELETE CASCADE,
  UNIQUE KEY uq_role_menu (role_id, menu_id)
);

-- ---------------------------------------------------------
-- 10. GENERIC UI TRANSLATIONS  (static text: "Login", "Username" ... not menu specific)
-- ---------------------------------------------------------
CREATE TABLE ui_translations (
  id              INT AUTO_INCREMENT PRIMARY KEY,
  translation_key VARCHAR(100) NOT NULL,
  language_id     INT NOT NULL,
  value           VARCHAR(255) NOT NULL,
  FOREIGN KEY (language_id) REFERENCES languages(id) ON DELETE CASCADE,
  UNIQUE KEY uq_key_lang (translation_key, language_id)
);

-- ---------------------------------------------------------
-- 11. LOGIN AUDIT
-- ---------------------------------------------------------
CREATE TABLE login_logs (
  id              INT AUTO_INCREMENT PRIMARY KEY,
  user_account_id INT NOT NULL,
  login_time      DATETIME DEFAULT CURRENT_TIMESTAMP,
  ip_address      VARCHAR(50),
  FOREIGN KEY (user_account_id) REFERENCES user_accounts(id) ON DELETE CASCADE
);

-- =========================================================
-- SEED: default menus (Dashboard is the only functional page for now)
-- =========================================================
INSERT INTO menus (id, parent_id, menu_key, route_path, icon, order_no) VALUES
(1, NULL, 'dashboard',        '/dashboard',            'LayoutDashboard', 1),
(2, NULL, 'employee',         NULL,                     'Users',           2),
(3, 2,    'employee.master',  '/employee/master',       'IdCard',          1),
(4, NULL, 'payroll',          NULL,                     'Wallet',          3),
(5, 4,    'payroll.process',  '/payroll/process',       'Banknote',        1),
(6, 4,    'payroll.reports',  NULL,                     'FileText',        2),
(7, 6,    'payroll.reports.monthly', '/payroll/reports/monthly', 'CalendarDays', 1),
(8, NULL, 'admin',            NULL,                     'Settings',        99),
(9, 8,    'admin.menu_builder', '/admin/menus',         'ListTree',        1),
(10,8,    'admin.employee_create', '/admin/employees/new', 'UserPlus',     2),
(11,8,    'admin.language',   '/admin/language',        'Languages',       3),
(12,8,    'admin.roles_access', '/admin/menu-access',   'ShieldCheck',     4);

-- Menu labels in all 4 languages
INSERT INTO menu_translations (menu_id, language_id, label) VALUES
(1,1,'Dashboard'), (1,2,'डैशबोर्ड'), (1,3,'ಡ್ಯಾಶ್‌ಬೋರ್ಡ್'), (1,4,'डॅशबोर्ड'),
(2,1,'Employee'), (2,2,'कर्मचारी'), (2,3,'ಉದ್ಯೋಗಿ'), (2,4,'कर्मचारी'),
(3,1,'Employee Master'), (3,2,'कर्मचारी मास्टर'), (3,3,'ಉದ್ಯೋಗಿ ಮಾಸ್ಟರ್'), (3,4,'कर्मचारी मास्टर'),
(4,1,'Payroll'), (4,2,'वेतन'), (4,3,'ವೇತನ'), (4,4,'वेतन'),
(5,1,'Process Payroll'), (5,2,'वेतन प्रक्रिया'), (5,3,'ವೇತನ ಪ್ರಕ್ರಿಯೆ'), (5,4,'वेतन प्रक्रिया'),
(6,1,'Reports'), (6,2,'रिपोर्ट'), (6,3,'ವರದಿಗಳು'), (6,4,'अहवाल'),
(7,1,'Monthly Report'), (7,2,'मासिक रिपोर्ट'), (7,3,'ಮಾಸಿಕ ವರದಿ'), (7,4,'मासिक अहवाल'),
(8,1,'Administration'), (8,2,'प्रशासन'), (8,3,'ಆಡಳಿತ'), (8,4,'प्रशासन'),
(9,1,'Menu Builder'), (9,2,'मेनू बिल्डर'), (9,3,'ಮೆನು ಬಿಲ್ಡರ್'), (9,4,'मेनू बिल्डर'),
(10,1,'Create Employee'), (10,2,'कर्मचारी बनाएं'), (10,3,'ಉದ್ಯೋಗಿ ರಚಿಸಿ'), (10,4,'कर्मचारी तयार करा'),
(11,1,'Language Settings'), (11,2,'भाषा सेटिंग्स'), (11,3,'ಭಾಷಾ ಸೆಟ್ಟಿಂಗ್‌ಗಳು'), (11,4,'भाषा सेटिंग्ज'),
(12,1,'Role Menu Access'), (12,2,'भूमिका मेनू पहुंच'), (12,3,'ಪಾತ್ರ ಮೆನು ಪ್ರವೇಶ'), (12,4,'भूमिका मेनू प्रवेश');

-- Give SuperAdmin (role_id=1) access to everything
INSERT INTO role_menu_permissions (role_id, menu_id, can_view, can_add, can_edit, can_delete)
SELECT 1, id, 1, 1, 1, 1 FROM menus;

-- Give every other role access to Dashboard only, by default (superadmin can extend later)
INSERT INTO role_menu_permissions (role_id, menu_id, can_view)
SELECT r.id, 1, 1 FROM roles r WHERE r.role_code <> 'SUPERADMIN';

-- A couple of common UI strings, for reference (frontend also ships static i18n json files)
INSERT INTO ui_translations (translation_key, language_id, value) VALUES
('login.title', 1, 'Login'), ('login.title', 2, 'लॉगिन'), ('login.title', 3, 'ಲಾಗಿನ್'), ('login.title', 4, 'लॉगिन'),
('login.username', 1, 'Username'), ('login.username', 2, 'उपयोगकर्ता नाम'), ('login.username', 3, 'ಬಳಕೆದಾರ ಹೆಸರು'), ('login.username', 4, 'वापरकर्तानाव'),
('login.password', 1, 'Password'), ('login.password', 2, 'पासवर्ड'), ('login.password', 3, 'ಪಾಸ್‌ವರ್ಡ್'), ('login.password', 4, 'पासवर्ड');

-- ---------------------------------------------------------
-- 12. SEED: one SuperAdmin employee + account
--     Default password = "Admin@123"  (bcrypt hash below, CHANGE after first login)
--     Hash generated with bcrypt, 10 rounds
-- ---------------------------------------------------------
INSERT INTO departments (name) VALUES ('Administration');
INSERT INTO designations (name) VALUES ('System Administrator');

INSERT INTO employees (employee_code, first_name, last_name, gender, email, department_id, designation_id, join_date, status)
VALUES ('EMP0001', 'Super', 'Admin', 'M', 'superadmin@hrms.local', 1, 1, CURDATE(), 'ACTIVE');

-- password_hash below corresponds to plaintext: Admin@123
INSERT INTO user_accounts (employee_id, role_id, username, password_hash, is_default, is_active)
VALUES (1, 1, 'superadmin', '$2b$10$m.MzzN.Ut3xkuoWIbu3ss.8Kb5m/z8Ife8tc6SkvtTj9AK4iAYNcS', 1, 1);
