using Dapper;
using HrmsBackend.Common;
using HrmsBackend.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using System.Data;
using System.Text.RegularExpressions;

namespace HrmsBackend.Controllers;

[ApiController]
[Route("api/api-rate-limit")]
[Authorize]
public class ApiRateLimitController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    private readonly EndpointDataSource _endpoints;

    public ApiRateLimitController(
        DbConnectionFactory db,
        EndpointDataSource endpoints)
    {
        _db = db;
        _endpoints = endpoints;
    }


    // =========================================================
    // CONSTANTS
    // =========================================================

    private static readonly string[] AllowedMethods =
    {
        "GET",
        "POST",
        "PUT",
        "PATCH",
        "DELETE"
    };

    // {id:int} / {id?} / {id=5}  ->  {id}
    private static readonly Regex RouteParamRegex =
        new(@"\{([^:}?=]+)[^}]*\}", RegexOptions.Compiled);


    // =========================================================
    // REQUEST MODELS
    // =========================================================

    public record CreateApiRateLimitRequest(
        string? ApiUrl,
        List<string>? HttpMethods,
        bool LoggedIn,
        int? RateLimit,
        int? LimitTime
    );

    public record UpdateApiRateLimitRequest(
        string? ApiUrl,
        string? HttpMethod,
        bool LoggedIn,
        int? RateLimit,
        int? LimitTime
    );


    // =========================================================
    // HELPERS
    // =========================================================

    private static string NormalizeUrl(string url)
    {
        url = url.Trim();

        return url.StartsWith('/') ? url : "/" + url;
    }

    // Returns an error message, or null when valid.
    private static string? ValidateCommon(
        string? apiUrl,
        int? rateLimit,
        int? limitTime)
    {
        if (string.IsNullOrWhiteSpace(apiUrl))
            return "API URL is required.";

        if (apiUrl.Length > 500)
            return "API URL cannot exceed 500 characters.";

        if (!rateLimit.HasValue || rateLimit.Value <= 0)
            return "Rate limit must be greater than 0.";

        if (!limitTime.HasValue || limitTime.Value <= 0)
            return "Limit time must be greater than 0.";

        return null;
    }


    // =========================================================
    // GET ALL API RATE LIMITS (ACTIVE + INACTIVE)
    // VIEW
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> GetApiRateLimits()
    {
        var user = this.GetAuthUser()!;

        using var conn = _db.CreateConnection();

        try
        {
            const string sql = @"
                SELECT
                    id,
                    api_url as apiUrl,
                    http_method as httpMethod,
                    logged_in as loggedIn,
                    rate_limit as rateLimit,
                    limit_time as limitTime,
                    is_active as isActive,
                    created_at as createdAt,
                    updated_at as updatedAt
                FROM master_api_rate_limit
                ORDER BY is_active DESC, id DESC";

            var data = await conn.QueryAsync(sql);

            return Ok(data);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message =
                    "Server error while loading API rate limits."
            });
        }
    }


    // =========================================================
    // GET AVAILABLE ENDPOINTS
    // Built from the registered routes, nothing hardcoded.
    // (declared before {id:int}; the int constraint keeps
    //  the two routes from clashing)
    // =========================================================

    [HttpGet("endpoints")]
    public IActionResult GetEndpoints()
    {
        var user = this.GetAuthUser()!;

        try
        {
            var data = _endpoints.Endpoints
                .OfType<RouteEndpoint>()
                .Select(e => new
                {
                    Action = e.Metadata.GetMetadata<ControllerActionDescriptor>(),
                    Methods = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods,
                    Raw = e.RoutePattern.RawText
                })
                // controller actions only (no swagger, health, static, etc.)
                .Where(x => x.Action != null && x.Methods != null && x.Raw != null)
                .Select(x => new
                {
                    Url = RouteParamRegex.Replace(NormalizeUrl(x.Raw!), "{$1}"),
                    Methods = x.Methods!
                })
                .Where(x => x.Url.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    apiUrl = g.Key,
                    methods = g
                        .SelectMany(x => x.Methods)
                        .Select(m => m.ToUpperInvariant())
                        .Where(m => AllowedMethods.Contains(m))
                        .Distinct()
                        .OrderBy(m => Array.IndexOf(AllowedMethods, m))
                        .ToList()
                })
                .Where(x => x.methods.Count > 0)
                .OrderBy(x => x.apiUrl, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Ok(data);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message =
                    "Server error while loading API endpoints."
            });
        }
    }


    // =========================================================
    // GET SINGLE API RATE LIMIT
    // VIEW
    // =========================================================

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetApiRateLimit(int id)
    {
        var user = this.GetAuthUser()!;

        using var conn = _db.CreateConnection();

        try
        {
            const string sql = @"
                SELECT
                    id,
                    api_url as apiUrl,
                    http_method as httpMethod,
                    logged_in as loggedIn,
                    rate_limit as rateLimit,
                    limit_time as limitTime,
                    is_active as isActive,
                    created_at as createdAt,
                    updated_at as updatedAt
                FROM master_api_rate_limit
                WHERE id = @id";

            var data =
                await conn.QueryFirstOrDefaultAsync(
                    sql,
                    new { id }
                );

            if (data == null)
            {
                return NotFound(new
                {
                    message =
                        "API rate limit record not found."
                });
            }

            return Ok(data);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message =
                    "Server error while loading API rate limit."
            });
        }
    }


    // =========================================================
    // CREATE
    // ONE API URL + MANY METHODS -> ONE ROW PER METHOD
    // =========================================================

    [HttpPost]
    public async Task<IActionResult> CreateApiRateLimit(
        [FromBody] CreateApiRateLimitRequest? body)
    {
        var user = this.GetAuthUser()!;

        if (body == null)
        {
            return BadRequest(new
            {
                message = "Request data is required."
            });
        }

        // -----------------------------------------------------
        // VALIDATION
        // -----------------------------------------------------

        var error = ValidateCommon(
            body.ApiUrl,
            body.RateLimit,
            body.LimitTime
        );

        if (error != null)
        {
            return BadRequest(new { message = error });
        }

        var methods = (body.HttpMethods ?? new List<string>())
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        if (methods.Count == 0)
        {
            return BadRequest(new
            {
                message = "Select at least one HTTP method."
            });
        }

        var invalid = methods
            .Where(m => !AllowedMethods.Contains(m))
            .ToList();

        if (invalid.Count > 0)
        {
            return BadRequest(new
            {
                message =
                    $"Invalid HTTP method: {string.Join(", ", invalid)}."
            });
        }

        var apiUrl = NormalizeUrl(body.ApiUrl!);


        try
        {
            using var conn = _db.CreateConnection();


            // -----------------------------------------------------
            // CHECK DUPLICATE ACTIVE RECORDS (all-or-nothing)
            // -----------------------------------------------------

            var existing =
                (await conn.QueryAsync<string>(
                    @"
                    SELECT http_method
                    FROM master_api_rate_limit
                    WHERE LOWER(api_url) = LOWER(@apiUrl)
                      AND http_method IN @methods
                      AND is_active = 1",
                    new
                    {
                        apiUrl,
                        methods
                    }
                )).ToList();

            if (existing.Count > 0)
            {
                return Conflict(new
                {
                    message =
                        $"An active rate limit already exists for {apiUrl} with: {string.Join(", ", existing)}. Nothing was created."
                });
            }


            // -----------------------------------------------------
            // INSERT ONE ROW PER METHOD (single transaction)
            // -----------------------------------------------------

            if (conn.State != ConnectionState.Open)
            {
                conn.Open();
            }

            using var tx = conn.BeginTransaction();

            const string sql = @"
                INSERT INTO master_api_rate_limit
                (
                    api_url,
                    http_method,
                    logged_in,
                    rate_limit,
                    limit_time,
                    is_active,
                    created_at,
                    updated_at
                )
                VALUES
                (
                    @apiUrl,
                    @httpMethod,
                    @loggedIn,
                    @rateLimit,
                    @limitTime,
                    1,
                    NOW(),
                    NOW()
                )";

            var ids = new List<int>();

            foreach (var httpMethod in methods)
            {
                await conn.ExecuteAsync(
                    sql,
                    new
                    {
                        apiUrl,
                        httpMethod,
                        loggedIn = body.LoggedIn ? 1 : 0,
                        rateLimit = body.RateLimit!.Value,
                        limitTime = body.LimitTime!.Value
                    },
                    tx
                );

                ids.Add(
                    await conn.ExecuteScalarAsync<int>(
                        "SELECT LAST_INSERT_ID()",
                        transaction: tx
                    )
                );
            }

            tx.Commit();


            return StatusCode(201, new
            {
                ids,
                message =
                    ids.Count == 1
                        ? "API rate limit created successfully."
                        : $"{ids.Count} API rate limits created successfully."
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message =
                    "Server error while creating API rate limit."
            });
        }
    }


    // =========================================================
    // UPDATE (ONE RECORD)
    // EDIT
    // =========================================================

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateApiRateLimit(
        int id,
        [FromBody] UpdateApiRateLimitRequest? body)
    {
        var user = this.GetAuthUser()!;

        if (body == null)
        {
            return BadRequest(new
            {
                message = "Request data is required."
            });
        }

        // -----------------------------------------------------
        // VALIDATION
        // -----------------------------------------------------

        var error = ValidateCommon(
            body.ApiUrl,
            body.RateLimit,
            body.LimitTime
        );

        if (error != null)
        {
            return BadRequest(new { message = error });
        }

        var httpMethod = body.HttpMethod?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(httpMethod))
        {
            return BadRequest(new
            {
                message = "HTTP method is required."
            });
        }

        if (!AllowedMethods.Contains(httpMethod))
        {
            return BadRequest(new
            {
                message = "Invalid HTTP method."
            });
        }

        var apiUrl = NormalizeUrl(body.ApiUrl!);


        try
        {
            using var conn = _db.CreateConnection();


            // -----------------------------------------------------
            // CHECK CURRENT RECORD
            // -----------------------------------------------------

            var exists =
                await conn.ExecuteScalarAsync<int?>(
                    @"
                    SELECT id
                    FROM master_api_rate_limit
                    WHERE id = @id
                      AND is_active = 1
                    LIMIT 1",
                    new { id }
                );

            if (!exists.HasValue)
            {
                return NotFound(new
                {
                    message =
                        "Active API rate limit record not found."
                });
            }


            // -----------------------------------------------------
            // CHECK DUPLICATE
            // -----------------------------------------------------

            var duplicate =
                await conn.ExecuteScalarAsync<int?>(
                    @"
                    SELECT id
                    FROM master_api_rate_limit
                    WHERE LOWER(api_url) = LOWER(@apiUrl)
                      AND http_method = @httpMethod
                      AND id <> @id
                      AND is_active = 1
                    LIMIT 1",
                    new
                    {
                        apiUrl,
                        httpMethod,
                        id
                    }
                );

            if (duplicate.HasValue)
            {
                return Conflict(new
                {
                    message =
                        "Another active rate limit for this API URL and HTTP method already exists."
                });
            }


            // -----------------------------------------------------
            // UPDATE
            // -----------------------------------------------------

            const string sql = @"
                UPDATE master_api_rate_limit
                SET
                    api_url = @apiUrl,
                    http_method = @httpMethod,
                    logged_in = @loggedIn,
                    rate_limit = @rateLimit,
                    limit_time = @limitTime,
                    updated_at = NOW()
                WHERE id = @id
                  AND is_active = 1";

            await conn.ExecuteAsync(
                sql,
                new
                {
                    id,
                    apiUrl,
                    httpMethod,
                    loggedIn = body.LoggedIn ? 1 : 0,
                    rateLimit = body.RateLimit!.Value,
                    limitTime = body.LimitTime!.Value
                }
            );


            return Ok(new
            {
                message =
                    "API rate limit updated successfully."
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message =
                    "Server error while updating API rate limit."
            });
        }
    }


    // =========================================================
    // SOFT DELETE
    // DEACTIVATE
    // =========================================================

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteApiRateLimit(
        int id)
    {
        var user = this.GetAuthUser()!;

        using var conn = _db.CreateConnection();

        try
        {
            // -----------------------------------------------------
            // CHECK RECORD
            // -----------------------------------------------------

            var record =
                await conn.QueryFirstOrDefaultAsync(
                    @"
                    SELECT
                        id,
                        api_url,
                        is_active
                    FROM master_api_rate_limit
                    WHERE id = @id",
                    new { id }
                );

            if (record == null)
            {
                return NotFound(new
                {
                    message =
                        "API rate limit record not found."
                });
            }

            if (Convert.ToInt32(record.is_active) == 0)
            {
                return BadRequest(new
                {
                    message =
                        "API rate limit is already inactive."
                });
            }


            // -----------------------------------------------------
            // SOFT DELETE
            // -----------------------------------------------------

            await conn.ExecuteAsync(
                @"
                UPDATE master_api_rate_limit
                SET
                    is_active = 0,
                    updated_at = NOW()
                WHERE id = @id",
                new { id }
            );


            return Ok(new
            {
                message =
                    "API rate limit deactivated successfully."
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message =
                    "Server error while deactivating API rate limit."
            });
        }
    }


    // =========================================================
    // ACTIVATE
    // RESTORE
    // =========================================================

    [HttpPut("{id:int}/activate")]
    public async Task<IActionResult> ActivateApiRateLimit(
        int id)
    {
        var user = this.GetAuthUser()!;

        using var conn = _db.CreateConnection();

        try
        {
            // -----------------------------------------------------
            // GET RECORD
            // -----------------------------------------------------

            var record =
                await conn.QueryFirstOrDefaultAsync(
                    @"
                    SELECT
                        id,
                        api_url,
                        http_method,
                        is_active
                    FROM master_api_rate_limit
                    WHERE id = @id",
                    new { id }
                );

            if (record == null)
            {
                return NotFound(new
                {
                    message =
                        "API rate limit record not found."
                });
            }

            if (Convert.ToInt32(record.is_active) == 1)
            {
                return BadRequest(new
                {
                    message =
                        "API rate limit is already active."
                });
            }


            // -----------------------------------------------------
            // CHECK DUPLICATE ACTIVE RECORD
            // -----------------------------------------------------

            var duplicate =
                await conn.ExecuteScalarAsync<int?>(
                    @"
                    SELECT id
                    FROM master_api_rate_limit
                    WHERE LOWER(api_url) = LOWER(@apiUrl)
                      AND http_method = @httpMethod
                      AND is_active = 1
                      AND id <> @id
                    LIMIT 1",
                    new
                    {
                        id,
                        apiUrl = (string)record.api_url,
                        httpMethod = (string)record.http_method
                    }
                );

            if (duplicate.HasValue)
            {
                return Conflict(new
                {
                    message =
                        "An active rate limit for this API URL and HTTP method already exists."
                });
            }


            // -----------------------------------------------------
            // ACTIVATE
            // -----------------------------------------------------

            await conn.ExecuteAsync(
                @"
                UPDATE master_api_rate_limit
                SET
                    is_active = 1,
                    updated_at = NOW()
                WHERE id = @id",
                new { id }
            );


            return Ok(new
            {
                message =
                    "API rate limit activated successfully."
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message =
                    "Server error while activating API rate limit."
            });
        }
    }
}