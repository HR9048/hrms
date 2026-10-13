namespace FileStorageServer.Options;

public class FileStorageOptions
{
    // Relative to the project's content root (e.g. "uploads/public"),
    // resolved to an absolute path in FilesController. Keeping these
    // relative means the whole FileStorageServer folder - code AND
    // uploaded files - can be copied/deployed as one unit.
    public string PublicRoot { get; set; } = string.Empty;
    public string PrivateRoot { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = string.Empty;

    // Server-to-server only (HrmsBackend -> this service). Never exposed to the browser.
    public string UploadApiKey { get; set; } = string.Empty;

    // Used to HMAC-sign short-lived secure download URLs.
    public string SecureSigningKey { get; set; } = string.Empty;

    public int SecureUrlLifetimeMinutes { get; set; } = 10;
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;
    public string[] AllowedExtensions { get; set; } = Array.Empty<string>();
}