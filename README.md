# HRMS (React + ASP.NET + MySQL)

Database-driven HRMS scaffold with:
- **Exactly two languages at a time**: English is always on. On first deploy you pick ONE more (Hindi, Kannada, or Marathi) — that becomes the system's second language. Only **Super Admin** can change which second language is active afterwards, from `Admin → Language Settings`.
- **Navbar language toggle** — shows only the two active languages; any logged-in user can switch between them live, and the navbar re-fetches translated menu labels from the DB.
- Modern, responsive UI — the navbar collapses into a hamburger menu on small screens, forms and tables reflow to a single column, and the login/setup screens are centered cards on a branded background.
- **Role-based, DB-driven navbar** with unlimited depth (menu → sub-menu → sub-sub-menu), built entirely by Super Admin in `Admin → Menu Builder`, with per-role visibility set in `Admin → Role Menu Access`.
- **Employee Master** + **multi-account login**: one employee can have several accounts (Employee, HR, Payroll, Manager...), each with its own username/password and role. After login, if a person has more than one account, an **Account Switcher** appears in the navbar letting them toggle between roles without re-entering credentials.
- Only **Dashboard** is a functional page today. Every other menu you create in the Menu Builder is real (stored in the DB, shown in the navbar, permission-gated) but renders a "coming soon" placeholder until you build that screen.

## 1. Database setup

```bash
mysql -u root -p < backend/db.sql
```

This creates the `hrms_db` schema, seeds the 4 languages, all roles, the starter menu tree (Dashboard, Employee, Payroll, Admin section), and one Super Admin login:

- **username:** `superadmin`
- **password:** `Admin@123`

Change this password after first login (via `Admin → Menu Builder → Create Employee`, add yourself a real account, then deactivate/remove the seed one).

## 2. Backend setup

```bash
cd backend
cp .env.example .env      # edit DB credentials + JWT_SECRET
npm install
npm run dev                # nodemon, http://localhost:5000
```

## 3. Frontend setup

```bash
cd frontend
npm install
npm run dev                 # http://localhost:5173 (proxies /api to :5000)
```

## 4. First run flow

1. Open the frontend — since `first_deploy_done = 0` in `system_settings`, you'll land on the language-choice screen. English is shown as always-on; pick Hindi, Kannada, or Marathi as the second language.
2. This sets `languages.is_active` (English + your pick only) and `system_settings.second_language_code`, then flips `first_deploy_done` to `1` — the screen won't show again.
3. You're taken to Login. Log in as `superadmin` / `Admin@123`.
4. From the navbar → Administration:
   - **Menu Builder** — create menus, sub-menus, sub-sub-menus with translated labels per language.
   - **Role Menu Access** — tick which roles can see which menu items.
   - **Create Employee** — add an employee to the Employee Master and give them one or more login accounts/roles.
   - **Language Settings** — change the system default language any time (Super Admin only).

## How multi-account login works

- `employees` = one row per real person.
- `user_accounts` = one row per (employee, role) pair — each with its own username/password.
- On login, the backend returns the account you logged into **plus** the list of sibling accounts (same `employee_id`).
- If there's more than one, the navbar shows an **Account Switcher**; picking another role calls `POST /api/auth/switch-account`, which re-issues a JWT scoped to that account/role — no re-login needed.

## Extending a placeholder menu into a real page

1. Create the menu in Menu Builder with a `route_path`, e.g. `/employee/master`.
2. Add a matching `<Route path="employee/master" element={...} />` inside `frontend/src/App.jsx`, and build the page component.
3. Grant the relevant roles `can_view` on that menu in Role Menu Access.
