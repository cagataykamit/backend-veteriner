using Backend.Veteriner.Application.Common.Abstractions;

namespace Backend.Veteriner.Application.Common.Email;

/// <summary>
/// IEmailAttachment aray�z�n� uygulayan basit DTO.
/// </summary>
public sealed class EmailAttachment : IEmailAttachment
{
    public string FileName { get; init; } = default!;
    public byte[] Content { get; init; } = default!;
    public string ContentType { get; init; } = "application/octet-stream";
}
