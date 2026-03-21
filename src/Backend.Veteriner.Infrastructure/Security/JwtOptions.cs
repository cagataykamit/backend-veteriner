namespace Backend.Veteriner.Infrastructure.Security;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "Backend.Veteriner";
    public string Audience { get; set; } = "Backend.Veteriner.Audience";
    public string Key { get; set; } = default!;
    public int ExpMinutes { get; init; } = 60;     // AccessToken s�resi (dakika)

    public int RefreshExpDays { get; set; } = 7;    // refresh token �mr� (g�n)
}