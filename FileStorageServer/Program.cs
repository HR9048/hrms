using System.Text;
using FileStorageServer.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection("FileStorage"));
builder.Services.AddControllers();

// Validates the SAME JWT that HrmsBackend already issues at login, so the
// frontend can reuse its existing token to call POST /secure/{id}/sign -
// no separate login against this service is needed.
//
// Matches HrmsBackend's own Program.cs exactly: a flat top-level JWT_SECRET
// (not a nested "Jwt" section), and no issuer/audience validation, since
// HrmsBackend's tokens are issued the same way (ValidateIssuer = false,
// ValidateAudience = false) - if this service validated issuer/audience
// while HrmsBackend's tokens carry none, every token would fail here.
var jwtSecret = builder.Configuration["JWT_SECRET"]
    ?? throw new InvalidOperationException(
        "JWT_SECRET is required and must match HrmsBackend's JWT_SECRET exactly.");

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

        // Without this, a failed token (expired, wrong signature...) just
        // comes back as a bare 401 with no body - this logs the real
        // reason to the server console instead.
        options.IncludeErrorDetails = true;
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Console.Error.WriteLine($"JWT validation failed: {context.Exception.Message}");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 50_000_000);

var app = builder.Build();

app.UseCors("Frontend");
app.UseStaticFiles(); // serves wwwroot/test.html at /test.html
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();