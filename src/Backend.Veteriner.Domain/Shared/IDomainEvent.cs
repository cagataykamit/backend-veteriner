

using MediatR;

namespace Backend.Veteriner.Domain.Shared;

/// <summary>
/// Domain event işaretleyicisi.
/// MediatR INotification üzerinden publish edilir.
/// </summary>
public interface IDomainEvent : INotification
{
    Guid EventId { get; }
    DateTime OccurredOnUtc { get; }
}