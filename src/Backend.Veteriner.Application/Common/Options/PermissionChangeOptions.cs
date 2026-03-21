namespace Backend.Veteriner.Application.Common.Options;

/// <summary>
/// Permission/role değişimlerinde oturum davranışı.
/// </summary>
public sealed class PermissionChangeOptions
{
    /// <summary>
    /// True ise: ilgili role bağlı kullanıcıların tüm refresh token'ları revoke edilir (LogoutAll).
    /// False ise: sadece cache düşürülür; yeni permission'lar refresh ile yansır.
    /// </summary>
    public bool RevokeSessionsOnPermissionChange { get; init; } = false;
}
