namespace Backend.Veteriner.Application.Common.Abstractions;

public interface IClientContext
{
    Guid? UserId { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
    string? Path { get; }
    string? Method { get; }
    string? CorrelationId { get; }
}