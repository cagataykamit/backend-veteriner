using Backend.Veteriner.Domain.Files;

namespace Backend.Veteriner.Application.Files.Contracts.Dtos;

/// <summary>Hazır dosyanın meta bilgisi; <see cref="Kind"/> <c>Image</c> | <c>Pdf</c>. Depolama anahtarı istemciye verilmez.</summary>
public sealed record StoredFileDto(
    Guid Id,
    string FileName,
    string ContentType,
    string Kind,
    long SizeBytes,
    string? Sha256,
    DateTime UploadedAtUtc,
    Guid UploadedByUserId)
{
    public static StoredFileDto From(StoredFile file)
        => new(
            file.Id,
            file.OriginalFileName,
            file.ContentType,
            file.Type.IsImage ? "Image" : "Pdf",
            file.SizeBytes,
            file.Sha256,
            file.ReadyAtUtc ?? file.CreatedAtUtc,
            file.UploadedByUserId);
}

/// <summary>İmzalı URL; <see cref="Url"/> API köküne görelidir.</summary>
public sealed record StoredFileAccessUrlDto(string Url, DateTime ExpiresAtUtc);

/// <summary>Akış ucu sonucu; akışı yanıt kapatır.</summary>
public sealed record StoredFileContent(Stream Content, string ContentType, string DownloadFileName, long SizeBytes);
