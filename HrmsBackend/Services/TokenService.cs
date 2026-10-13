using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace HrmsBackend.Services;

public record AccountForToken(int Id, int EmployeeId, int RoleId, string RoleCode, string Username);

/// <summary>
/// Equivalent of src/utils/jwt.js. Signs tokens with the same claim names
/// (accountId, employeeId, roleId, roleCode, username) and the same HS256 +
/// JWT_SECRET as the original app, so the two backends are token-compatible
/// during a migration window if that's ever useful.
/// </summary>
public class TokenService
{
    private readonly string _secret;
    private readonly TimeSpan _expiresIn;

    public TokenService(IConfiguration config)
    {
        _secret = config["JWT_SECRET"]
            ?? throw new InvalidOperationException("JWT_SECRET is not configured.");
        _expiresIn = ParseExpiry(config["JWT_EXPIRES_IN"] ?? "8h");
    }

    public string SignToken(AccountForToken account)
    {
        var claims = new[]
        {
            new Claim("accountId", account.Id.ToString()),
            new Claim("employeeId", account.EmployeeId.ToString()),
            new Claim("roleId", account.RoleId.ToString()),
            new Claim("roleCode", account.RoleCode),
            new Claim("username", account.Username)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.Add(_expiresIn),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Parses simple duration strings like "8h", "30m", "1d", "45s", matching the
    /// handful of formats commonly used with JWT_EXPIRES_IN (and Node's `ms` package).
    /// Falls back to 8 hours if the value can't be parsed.
    /// </summary>
    private static TimeSpan ParseExpiry(string value)
    {
        value = value.Trim();

        if (double.TryParse(value, out var rawSeconds))
        {
            return TimeSpan.FromSeconds(rawSeconds);
        }

        if (value.Length >= 2)
        {
            var unit = value[^1];
            var numberPart = value[..^1];
            if (double.TryParse(numberPart, out var amount))
            {
                return unit switch
                {
                    's' => TimeSpan.FromSeconds(amount),
                    'm' => TimeSpan.FromMinutes(amount),
                    'h' => TimeSpan.FromHours(amount),
                    'd' => TimeSpan.FromDays(amount),
                    _ => TimeSpan.FromHours(8)
                };
            }
        }

        return TimeSpan.FromHours(8);
    }
}
