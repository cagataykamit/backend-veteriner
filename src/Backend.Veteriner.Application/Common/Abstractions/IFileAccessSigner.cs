namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Kısa ömürlü dosya erişim URL'si imzası (<c>&lt;img src&gt;</c> Bearer gönderemez; ASAMA6 R3). İmza dosya kimliği ve
/// bitiş zamanını bağlar; süre denetimi çağıranındır.
/// </summary>
public interface IFileAccessSigner
{
    string Sign(Guid fileId, long expiresUnixSeconds);

    bool IsValid(Guid fileId, long expiresUnixSeconds, string? signature);
}
