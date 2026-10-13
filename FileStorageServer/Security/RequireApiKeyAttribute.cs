using FileStorageServer.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace FileStorageServer.Security;

/// <summary>
/// Checks the X-Api-Key header against FileStorage:UploadApiKey. Used only on
/// upload/delete - endpoints that must never be reachable directly from a
/// browser, since the key would then be visible to anyone with dev tools open.
/// </summary>
public class RequireApiKeyAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var options = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<FileStorageOptions>>().Value;

        if (!context.HttpContext.Request.Headers.TryGetValue("X-Api-Key", out var provided) ||
            provided.Count == 0 ||
            !CryptographicallyEqual(provided[0] ?? string.Empty, options.UploadApiKey))
        {
            context.Result = new UnauthorizedObjectResult(new { message = "Invalid or missing API key." });
            return;
        }

        await next();
    }

    private static bool CryptographicallyEqual(string a, string b)
    {
        var aBytes = System.Text.Encoding.UTF8.GetBytes(a);
        var bBytes = System.Text.Encoding.UTF8.GetBytes(b);
        return aBytes.Length == bBytes.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
