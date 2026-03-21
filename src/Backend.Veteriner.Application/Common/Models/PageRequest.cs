namespace Backend.Veteriner.Application.Common.Models;

public sealed class PageRequest
{
    public int Page { get; init; } = 1;        // 1-based
    public int PageSize { get; init; } = 20;   // default 20
    public string? Sort { get; init; }         // örn: "createdAtUtc"
    public string? Order { get; init; } = "asc"; // asc|desc
    public string? Search { get; init; }       // opsiyonel
}