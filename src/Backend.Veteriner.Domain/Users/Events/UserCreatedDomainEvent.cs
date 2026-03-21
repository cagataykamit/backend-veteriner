using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Users.Events;

/// <summary>
/// Yeni kullanıcı oluşturulduğunda üretilir.
/// </summary>
public sealed record UserCreatedDomainEvent(
    Guid UserId,
    string Email
) : DomainEvent;