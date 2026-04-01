namespace Backend.Veteriner.Application.Common.Models;

/// <summary>Sayfalama + opsiyonel sıralama/arama alanları. Her liste endpointi bu alanların hepsini uygulamaz; ilgili action XML açıklamasına bakın.</summary>
public sealed class PageRequest
{
    public int Page { get; init; } = 1;        // 1-based
    public int PageSize { get; init; } = 20;   // default 20
    public string? Sort { get; init; }         // örn: "createdAtUtc"
    public string? Order { get; init; } = "asc"; // asc|desc
    /// <summary>Metin araması; endpoint işlemediyse yok sayılır (Swagger’da görünür olabilir).</summary>
    public string? Search { get; init; }
}