using System.Net.Http.Json;

namespace HrmsBackend.Services;

public record FileUploadResult(string fileId, string storedFileName, string? originalFileName, string type, string? url);
public record SignedUrlResult(string url, long expiresAt);

public class FileStorageClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public FileStorageClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _http.BaseAddress = new Uri(config["FileStorage:BaseUrl"]!);
        _apiKey = config["FileStorage:UploadApiKey"]!;
    }

    public async Task<FileUploadResult> UploadAsync(IFormFile file, string type, string? category = null)
    {
        using var content = new MultipartFormDataContent();
        await using var stream = file.OpenReadStream();
        content.Add(new StreamContent(stream), "file", file.FileName);

        var query = $"?type={type}" + (string.IsNullOrEmpty(category) ? "" : $"&category={Uri.EscapeDataString(category)}");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/files/upload{query}") { Content = content };
        request.Headers.Add("X-Api-Key", _apiKey);

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FileUploadResult>())!;
    }

    // Asks the file server for a short-lived signed URL (secure files only).
    public async Task<SignedUrlResult> SignAsync(string fileId, bool download = false, string? downloadName = null)
    {
        var query = download ? $"?download=true&name={Uri.EscapeDataString(downloadName ?? "")}" : "";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/files/secure/{fileId}/sign{query}");
        request.Headers.Add("X-Api-Key", _apiKey);

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SignedUrlResult>())!;
    }

    public async Task DeleteAsync(string type, string fileId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/files/{type}/{fileId}");
        request.Headers.Add("X-Api-Key", _apiKey);

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}