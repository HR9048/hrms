using System.Net;
using System.Text.RegularExpressions;
using HrmsBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrmsBackend.Controllers;

// Single generic endpoint set for every module: the browser only ever talks to HrmsBackend.
[ApiController]
[Route("api/files")]
[Authorize]
public class FilesProxyController : ControllerBase
{
    private static readonly Regex FileIdPattern = new("^[0-9a-f]{32}$", RegexOptions.Compiled);

    private readonly FileStorageClient _files;

    public FilesProxyController(FileStorageClient files) => _files = files;

    // POST /api/files/upload?type=public|secure&category=employee-docs
    [HttpPost("upload")]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> Upload(IFormFile? file, [FromQuery] string type = "secure", [FromQuery] string? category = null)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file uploaded." });

        if (type != "public" && type != "secure")
            return BadRequest(new { message = "type must be 'public' or 'secure'." });

        try
        {
            return Ok(await _files.UploadAsync(file, type, category));
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
        {
            return BadRequest(new { message = "The file server rejected this file (type or size not allowed)." });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { message = "File storage service is unreachable.", detail = ex.Message });
        }
    }

    // GET /api/files/{fileId}/link?download=true&name=original.pdf
    // Secure files only; public files already have a permanent url from the upload response.
    [HttpGet("{fileId}/link")]
    public async Task<IActionResult> GetLink(string fileId, [FromQuery] bool download = false, [FromQuery] string? name = null)
    {
        if (!FileIdPattern.IsMatch(fileId))
            return BadRequest(new { message = "Invalid file id." });

        // TODO (employee master): check here that the logged-in user is allowed to see this file.
        try
        {
            return Ok(await _files.SignAsync(fileId, download, name));
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return NotFound(new { message = "File not found." });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { message = "File storage service is unreachable.", detail = ex.Message });
        }
    }

    // DELETE /api/files/{type}/{fileId}
    [HttpDelete("{type}/{fileId}")]
    public async Task<IActionResult> Delete(string type, string fileId)
    {
        if ((type != "public" && type != "secure") || !FileIdPattern.IsMatch(fileId))
            return BadRequest(new { message = "Invalid request." });

        try
        {
            await _files.DeleteAsync(type, fileId);
            return Ok(new { message = "File deleted." });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return NotFound(new { message = "File not found." });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { message = "File storage service is unreachable.", detail = ex.Message });
        }
    }
}