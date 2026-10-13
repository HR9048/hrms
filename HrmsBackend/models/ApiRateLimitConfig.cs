namespace HrmsBackend.Models;

public class ApiRateLimitConfig
{
    public int Id { get; set; }

    public string ApiUrl { get; set; } = string.Empty;

    public string? HttpMethod { get; set; }

    public bool LoggedIn { get; set; }

    public int RateLimit { get; set; }

    public int LimitTime { get; set; }

    public bool IsActive { get; set; }
}