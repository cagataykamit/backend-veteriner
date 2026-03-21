namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// E-posta ile g�nderilecek bir dosya ekinin temel s�zle�mesi.
/// </summary>
public interface IEmailAttachment
{
    string FileName { get; }
    byte[] Content { get; }
    string ContentType { get; }
}
