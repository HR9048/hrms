using FileStorageServer.Options;
using FileStorageServer.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;

namespace FileStorageServer.Controllers;

[ApiController]
[Route("api/files")]
public class FilesController : ControllerBase
{
    private readonly FileStorageOptions _options;
    private readonly string _publicRoot;
    private readonly string _privateRoot;
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    public FilesController(IOptions<FileStorageOptions> options, IWebHostEnvironment env)
    {
        _options = options.Value;

        // Resolve relative to the project's own folder (ContentRootPath),
        // not whatever the current working directory happens to be, so
        // this works the same under `dotnet run`, `dotnet FileStorageServer.dll`,
        // or as a Windows Service with a different startup directory.
        _publicRoot = Path.GetFullPath(Path.Combine(env.ContentRootPath, _options.PublicRoot));
        _privateRoot = Path.GetFullPath(Path.Combine(env.ContentRootPath, _options.PrivateRoot));

        Directory.CreateDirectory(_publicRoot);
        Directory.CreateDirectory(_privateRoot);
    }

    private string RootFor(string type) => type == "public" ? _publicRoot : _privateRoot;

    // =========================================================
    // UPLOAD
    // Server-to-server only (X-Api-Key from HrmsBackend). Never called
    // directly from the browser, so the key never reaches the client.
    // POST /api/files/upload?type=public|secure&category=employee-docs
    //
    // "category" is purely an on-disk organization aid (e.g. so
    // uploads/private/employee-docs/, uploads/private/payslips/ etc. stay
    // separate and easy to browse/back up). It plays no role in access
    // control - that's still entirely type (public/secure) + signature.
    // =========================================================
    [HttpPost("upload")]
    [RequireApiKey]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> Upload(IFormFile? file, [FromQuery] string type = "secure", [FromQuery] string? category = null)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file uploaded." });

        if (type != "public" && type != "secure")
            return BadRequest(new { message = "type must be 'public' or 'secure'." });

        if (!string.IsNullOrEmpty(category) && !IsSafeCategory(category))
            return BadRequest(new { message = "category can only contain letters, numbers, hyphens and underscores." });

        if (file.Length > _options.MaxFileSizeBytes)
            return BadRequest(new { message = $"File exceeds the {_options.MaxFileSizeBytes / 1024 / 1024} MB limit." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_options.AllowedExtensions.Contains(ext))
            return BadRequest(new { message = $"File type '{ext}' is not allowed." });

        var fileId = Guid.NewGuid().ToString("N");
        var storedFileName = fileId + ext;

        var folder = string.IsNullOrEmpty(category) ? RootFor(type) : Path.Combine(RootFor(type), category);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, storedFileName);

        await using (var stream = System.IO.File.Create(path))
        {
            await file.CopyToAsync(stream);
        }

        if (type == "public")
        {
            return Ok(new
            {
                fileId,
                storedFileName,
                originalFileName = file.FileName,
                type,
                category,
                url = $"{_options.PublicBaseUrl}/api/files/public/{storedFileName}"
            });
        }

        // Secure files never get a directly-browsable URL back - the caller
        // has to request a short-lived signed one via /secure/{fileId}/sign
        // whenever it actually needs to show/download the file.
        return Ok(new
        {
            fileId,
            storedFileName,
            originalFileName = file.FileName,
            type,
            category
        });
    }



    // =========================================================
    // PUBLIC FILES - no auth, permanent URL.
    // GET /api/files/public/{storedFileName}
    // =========================================================
    [HttpGet("public/{storedFileName}")]
    public IActionResult GetPublicFile(string storedFileName, [FromQuery] bool download = false, [FromQuery] string? name = null)
    {
        if (!IsSafeStoredFileName(storedFileName))
            return BadRequest();

        var path = Directory.EnumerateFiles(_publicRoot, storedFileName, SearchOption.AllDirectories).FirstOrDefault();
        if (path == null)
            return NotFound();

        var full = Path.GetFullPath(path);
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        return download
            ? PhysicalFile(full, GetContentType(full), SafeDownloadName(name, storedFileName))
            : PhysicalFile(full, GetContentType(full));
    }

    // Server-to-server only now (HrmsBackend sends X-Api-Key). The browser never calls this.
    [HttpPost("secure/{fileId}/sign")]
    [RequireApiKey]
    public IActionResult SignSecureUrl(string fileId, [FromQuery] bool download = false, [FromQuery] string? name = null)
    {
        if (!IsSafeFileId(fileId))
            return BadRequest();

        if (name is { Length: > 200 })
            return BadRequest(new { message = "name is too long." });

        if (FindSecureFile(fileId) == null)
            return NotFound(new { message = "File not found." });

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.SecureUrlLifetimeMinutes).ToUnixTimeSeconds();
        var signature = SecureUrlSigner.Sign(fileId, expiresAt, _options.SecureSigningKey, download, name);

        var url = $"{_options.PublicBaseUrl}/api/files/secure/{fileId}?exp={expiresAt}&sig={signature}";
        if (download)
            url += $"&download=true&name={Uri.EscapeDataString(name ?? "")}";

        return Ok(new { url, expiresAt });
    }

    [HttpGet("secure/{fileId}")]
    public IActionResult GetSecureFile(string fileId, [FromQuery] long exp, [FromQuery] string? sig,
        [FromQuery] bool download = false, [FromQuery] string? name = null)
    {
        if (!IsSafeFileId(fileId))
            return BadRequest();

        if (!SecureUrlSigner.Validate(fileId, exp, sig, _options.SecureSigningKey, download, name))
            return Unauthorized(new { message = "This link is invalid or has expired. Request a new one." });

        var path = FindSecureFile(fileId);
        if (path == null)
            return NotFound();

        var full = Path.GetFullPath(path);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        return download
            ? PhysicalFile(full, GetContentType(full), SafeDownloadName(name, Path.GetFileName(full)))
            : PhysicalFile(full, GetContentType(full));
    }

    // Helper: strips path characters from the user-visible download name
    private static string SafeDownloadName(string? name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;
        var invalid = Path.GetInvalidFileNameChars();
        var clean = string.Concat(name.Where(c => !invalid.Contains(c))).Trim();
        if (clean.Length == 0) return fallback;
        return clean.Length > 150 ? clean[..150] : clean;
    }
    // =========================================================
    // DELETE - server-to-server only, same as upload.
    // DELETE /api/files/{type}/{fileId}
    // =========================================================
    [HttpDelete("{type}/{fileId}")]
    [RequireApiKey]
    public IActionResult Delete(string type, string fileId)
    {
        if (type != "public" && type != "secure")
            return BadRequest(new { message = "type must be 'public' or 'secure'." });

        if (!IsSafeFileId(fileId))
            return BadRequest();

        var match = Directory.EnumerateFiles(RootFor(type), fileId + ".*", SearchOption.AllDirectories).FirstOrDefault();
        if (match == null)
            return NotFound();

        System.IO.File.Delete(match);
        return Ok(new { message = "File deleted." });
    }

    // ---------------------------------------------------------
    // HELPERS
    // ---------------------------------------------------------

    private string? FindSecureFile(string fileId) =>
        Directory.EnumerateFiles(_privateRoot, fileId + ".*", SearchOption.AllDirectories).FirstOrDefault();

    // fileId is always a GUID ("N" format: 32 lowercase hex chars) that we
    // generated ourselves, so this doubles as strict input validation and
    // blocks any path-traversal attempt via the route value.
    private static bool IsSafeFileId(string fileId) =>
        !string.IsNullOrEmpty(fileId) &&
        fileId.Length == 32 &&
        fileId.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));

    // Letters, numbers, hyphens, underscores only - no "..", "/" or "\",
    // so a category can never be used to write/read outside its own root.
    private static bool IsSafeCategory(string category) =>
        category.Length <= 50 && category.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');

    private static bool IsSafeStoredFileName(string storedFileName)
    {
        if (string.IsNullOrEmpty(storedFileName)) return false;
        var nameOnly = Path.GetFileNameWithoutExtension(storedFileName);
        var ext = Path.GetExtension(storedFileName);
        return IsSafeFileId(nameOnly) && ext.Length is > 1 and <= 10 && !ext.Contains('/') && !ext.Contains('\\');
    }

    private static string GetContentType(string path)
    {
        if (!ContentTypeProvider.TryGetContentType(path, out var contentType))
            contentType = "application/octet-stream";
        return contentType;
    }
}