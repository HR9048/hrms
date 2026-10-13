using System.Data;
using Dapper;
using HrmsBackend.Data;
using HrmsBackend.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HrmsBackend.Controllers;

[ApiController]
[Route("api/system")]
public class SystemController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    public SystemController(DbConnectionFactory db) => _db = db;

    public record LanguageBody(string? LanguageCode);

    // These two use their own short-lived connection (like the original
    // getSetting/setSetting, which used the global `pool.query` rather
    // than the transactional `conn`) — so, faithfully, they are NOT part
    // of the same transaction as ActivateSecondLanguage below.
    private async Task<string?> GetSetting(string key)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT setting_value FROM system_settings WHERE setting_key = @key", new { key });
    }

    private async Task SetSetting(string key, string value)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "INSERT INTO system_settings (setting_key, setting_value) VALUES (@key, @value) " +
            "ON DUPLICATE KEY UPDATE setting_value = @value",
            new { key, value });
    }

    // Turns exactly ONE non-English language on, and every other non-English language off.
    private static async Task ActivateSecondLanguage(MySqlConnection conn, MySqlTransaction tx, string languageCode)
    {
        if (languageCode == "en")
        {
            throw new InvalidOperationException(
                "English is always active by default and cannot be selected as the second language");
        }

        var langId = await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT id FROM languages WHERE code = @languageCode AND code <> 'en'",
            new { languageCode }, tx);
        if (langId == null)
        {
            throw new InvalidOperationException("Invalid language code");
        }

        await conn.ExecuteAsync("UPDATE languages SET is_active = 0 WHERE code NOT IN ('en', 'hi')", transaction: tx);
        await conn.ExecuteAsync("UPDATE languages SET is_active = 1 WHERE code = @languageCode", new { languageCode }, tx);
    }

    // ---------------------------------------------------------
    // GET /api/system/status — public
    // ---------------------------------------------------------
    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        try
        {
            var firstDeployDone = (await GetSetting("first_deploy_done")) == "1";
            var secondLanguageCode = await GetSetting("second_language_code");
            var organizationSetupDone = (await GetSetting("organization_setup_done")) == "1";

            return Ok(new
            {
                firstDeployDone,
                secondLanguageCode = string.IsNullOrEmpty(secondLanguageCode) ? null : secondLanguageCode,
                organizationSetupDone
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error" });
        }
    }

    // ---------------------------------------------------------
    // POST /api/system/first-deploy-language — public, but only works ONCE
    // ---------------------------------------------------------
    [HttpPost("first-deploy-language")]
    public async Task<IActionResult> FirstDeployLanguage([FromBody] LanguageBody? body)
    {
        var firstDeployDone = (await GetSetting("first_deploy_done")) == "1";
        if (firstDeployDone)
        {
            return StatusCode(403, new
            {
                message = "The system language has already been set. Ask a Super Admin to change it."
            });
        }

        var languageCode = body?.LanguageCode ?? "";

        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();
        try
        {
            await ActivateSecondLanguage(conn, tx, languageCode);
            await tx.CommitAsync();

            await SetSetting("second_language_code", languageCode);
            await SetSetting("first_deploy_done", "1");

            return Ok(new { message = "Language set successfully", languageCode });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.Error.WriteLine(ex);
            return BadRequest(new { message = string.IsNullOrEmpty(ex.Message) ? "Server error" : ex.Message });
        }
    }

    // ---------------------------------------------------------
    // PUT /api/system/second-language — SuperAdmin only, any time after deploy
    // ---------------------------------------------------------
    [HttpPut("second-language")]
    [Authorize]
    [RequireRole("SUPERADMIN")]
    public async Task<IActionResult> SecondLanguage([FromBody] LanguageBody? body)
    {
        var languageCode = body?.LanguageCode ?? "";

        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();
        try
        {
            await ActivateSecondLanguage(conn, tx, languageCode);
            await tx.CommitAsync();

            await SetSetting("second_language_code", languageCode);

            return Ok(new { message = "System language updated", languageCode });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.Error.WriteLine(ex);
            return BadRequest(new { message = string.IsNullOrEmpty(ex.Message) ? "Server error" : ex.Message });
        }
    }
}
