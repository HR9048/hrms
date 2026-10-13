# HRMS Backend — ASP.NET Core 8 port

This is a 1:1 functional port of the original Node.js/Express backend
(`hrms-backend`) to C#/.NET 8, using the same MySQL database, the same
route paths, the same request/response JSON shapes, and the same
auth/role rules — so the existing React frontend should work against
it unchanged.

| Original (Node)              | Here (C#/.NET)                          |
|-------------------------------|------------------------------------------|
| Express                       | ASP.NET Core 8 Web API (Controllers)     |
| mysql2                        | MySqlConnector + Dapper                  |
| jsonwebtoken                  | Microsoft.AspNetCore.Authentication.JwtBearer |
| bcryptjs                      | BCrypt.Net-Next                          |
| `middleware/auth.js`          | JWT Bearer authentication (`[Authorize]`)|
| `middleware/role.js`          | `[RequireRole("SUPERADMIN", ...)]` filter|
| `.env`                        | `appsettings.json` (same key names)      |

## Project layout

```
HrmsBackend/
  Program.cs                     — app startup (was server.js)
  appsettings.json                — config (was .env)
  db.sql                          — unchanged schema reference
  Data/DbConnectionFactory.cs     — was db.js
  Services/TokenService.cs        — was utils/jwt.js
  Common/AuthUser.cs              — reads the token claims (was req.user)
  Common/DapperRowExtensions.cs   — preserves raw column-name JSON output
  Filters/RequireRoleAttribute.cs — was middleware/role.js
  Controllers/
    AuthController.cs             — was routes/auth.js
    LanguagesController.cs        — was routes/languages.js
    SystemController.cs           — was routes/system.js
    MenusController.cs            — was routes/menus.js
    EmployeesController.cs        — was routes/employees.js
    AdminController.cs            — was routes/admin.js
```

## Running it

1. Install the .NET 8 SDK.
2. Edit `appsettings.json` (or set the equivalent environment variables —
   same names as your old `.env`: `DB_HOST`, `DB_USER`, `DB_PASSWORD`,
   `DB_NAME`, `DB_PORT`, `JWT_SECRET`, `JWT_EXPIRES_IN`, `PORT`).
   Don't commit real secrets to `appsettings.json` — use environment
   variables or `dotnet user-secrets` in production.
3. Restore & run:
   ```
   cd HrmsBackend
   dotnet restore
   dotnet run
   ```
4. It listens on `http://0.0.0.0:<PORT>` (default 5000), same as the
   Node app, with `GET /api/health` returning `{ "status": "ok" }`.

I wasn't able to `dotnet restore`/build this in my sandbox (no .NET SDK
or NuGet access here), so please do a build on your machine before
relying on it — I've been careful with the API surface, but a first
compile is worth doing.

## Important — please read before running

**Table name mismatch already present in your original code.** Your
`db.sql` creates a table called `employees`, but every query in
`auth.js` and `employees.js` (and therefore in this port too) reads
from `employee_master`. This isn't something I introduced — it was
already true of the Node app, which means it can't have been running
successfully against a fresh copy of this exact `db.sql`. You'll need
to either rename the table to `employee_master` in your schema (or in
your live database, if it already differs from this file) before
either backend will work. I deliberately did **not** silently rename
it in the port, since I don't know which side reflects your actual
database.

## Behavioral parity notes

- **JSON casing is intentionally inconsistent, matching the original.**
  Endpoints that build a response object by hand in the JS code (e.g.
  login's `account` object) use camelCase (`accountId`, `roleCode`,
  ...). Endpoints that do `res.json(rows)` — forwarding a raw MySQL
  result set — keep the original snake_case column/alias names
  (`role_code`, `department_name`, `is_active`, ...). This port
  reproduces that exactly, endpoint by endpoint, rather than
  normalizing everything to camelCase, so no frontend changes should
  be needed.
- **TINYINT(1) columns** (`is_active`, `can_view`, ...) come back as
  the numbers `0`/`1` in passthrough responses, matching mysql2's
  default behavior, rather than .NET's usual `true`/`false`
  (`TreatTinyAsBoolean=false` in the connection string handles this).
- **Auth error messages** ("No token provided" / "Invalid or expired
  token" / "Not authenticated" / "You do not have permission to
  perform this action") are reproduced verbatim via custom JWT
  bearer/authorization handlers, in case the frontend matches on them.
- **Password hashes are compatible.** BCrypt.Net-Next produces/verifies
  standard bcrypt hashes, so the existing bcrypt hashes in your
  database (including the seeded `superadmin` / `Admin@123` account)
  will keep working — no re-hashing needed.
- **JWTs are compatible.** Tokens are signed HS256 with the same
  secret and the same claim names (`accountId`, `employeeId`,
  `roleId`, `roleCode`, `username`), so a token issued by the old
  Node backend will validate here too, and vice versa, if that's
  useful during a cutover.
- **CORS** is wide open (`AllowAnyOrigin/Method/Header`), matching the
  Node app's unconfigured `cors()`.
- The `system.js` first-deploy/second-language transaction handling —
  including the fact that `first_deploy_done`/`second_language_code`
  writes happen *outside* the transaction that flips the language
  rows — is preserved exactly as in the original, quirks included.

## What wasn't ported

Nothing was intentionally dropped — all 6 route files, every endpoint,
and both middlewares are implemented. `package-lock.json`/`node_modules`
obviously have no C# equivalent; NuGet packages are declared in
`HrmsBackend.csproj` instead.
