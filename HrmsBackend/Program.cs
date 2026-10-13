/*using System.Text;
using Dapper;
using HrmsBackend.Data;
using HrmsBackend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

// mysql2 doesn't need this, but Dapper does: this lets Dapper map snake_case
// DB columns (role_code, first_name, ...) onto PascalCase C# properties
// (RoleCode, FirstName, ...) automatically for the typed query results.
Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<DbConnectionFactory>();
builder.Services.AddSingleton<TokenService>();

var jwtSecret = builder.Configuration["JWT_SECRET"]
    ?? throw new InvalidOperationException(
        "JWT_SECRET is required. Set it in appsettings.json or as an environment variable.");

// Equivalent of middleware/auth.js's requireAuth: verifies the Bearer token
// and populates the same claims (accountId, employeeId, roleId, roleCode, username)
// that req.user carried in the Node app.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.Zero
        };

        // Reproduces the exact response bodies/messages from requireAuth:
        // "No token provided" when there's no Bearer header, "Invalid or expired
        // token" when there is one but it doesn't validate.
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";

                var authHeader = context.Request.Headers.Authorization.ToString();
                var hasToken = authHeader.StartsWith("Bearer ");
                var message = hasToken ? "Invalid or expired token" : "No token provided";

                await context.Response.WriteAsJsonAsync(new { message });
            }
        };
    });

builder.Services.AddAuthorization();

// Equivalent of app.use(cors()) with no options: wide open, matches every origin.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader());
});

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Equivalent of app.get('/api/health', ...)
app.MapGet("/api/health", () => Results.Json(new { status = "ok" }));

var port = builder.Configuration["PORT"] ?? "5000";
app.Urls.Add($"http://0.0.0.0:{port}");

app.Run();
*/

using System.Text;
using Dapper;
using HrmsBackend.Data;
using HrmsBackend.Services;
using HrmsBackend.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

// mysql2 doesn't need this, but Dapper does: this lets Dapper map snake_case
// DB columns (role_code, first_name, ...) onto PascalCase C# properties
// (RoleCode, FirstName, ...) automatically for the typed query results.
Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddSingleton<DbConnectionFactory>();
builder.Services.AddSingleton<TokenService>();

// Dynamic API rate-limit service
builder.Services.AddSingleton<ApiRateLimitService>();
builder.Services.AddHttpClient<FileStorageClient>();

var jwtSecret = builder.Configuration["JWT_SECRET"]
    ?? throw new InvalidOperationException(
        "JWT_SECRET is required. Set it in appsettings.json or as an environment variable.");


// ============================================================
// JWT AUTHENTICATION
// ============================================================

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            IssuerSigningKey =
                new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtSecret)),

            ClockSkew = TimeSpan.Zero
        };

        // Reproduces the exact response bodies/messages from requireAuth
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();

                context.Response.StatusCode =
                    StatusCodes.Status401Unauthorized;

                context.Response.ContentType =
                    "application/json";

                var authHeader =
                    context.Request.Headers.Authorization.ToString();

                var hasToken =
                    authHeader.StartsWith("Bearer ");

                var message =
                    hasToken
                        ? "Invalid or expired token"
                        : "No token provided";

                await context.Response.WriteAsJsonAsync(
                    new { message });
            }
        };
    });

builder.Services.AddAuthorization();


// ============================================================
// CORS
// ============================================================

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
});


// ============================================================
// BUILD APPLICATION
// ============================================================

var app = builder.Build();


// ============================================================
// MIDDLEWARE PIPELINE
// ============================================================

app.UseCors();

app.UseAuthentication();

app.UseMiddleware<ApiRateLimitMiddleware>();

app.UseAuthorization();


// ============================================================
// CONTROLLERS
// ============================================================

app.MapControllers();


// ============================================================
// HEALTH CHECK
// ============================================================

app.MapGet(
    "/api/health",
    () => Results.Json(new { status = "ok" })
);


// ============================================================
// SERVER PORT
// ============================================================

var port = builder.Configuration["PORT"] ?? "5000";

app.Urls.Add($"http://0.0.0.0:{port}");

app.Run();