using Backend.Veteriner.Domain.Users.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Users.EventHandlers;

/// <summary>
/// UserCreated domain event'ini işler.
/// İlk aşamada log üretir; sonraki aşamada email verification / onboarding / audit tetiklenebilir.
/// </summary>
public sealed class UserCreatedDomainEventHandler : INotificationHandler<UserCreatedDomainEvent>
{
    private readonly ILogger<UserCreatedDomainEventHandler> _logger;

    public UserCreatedDomainEventHandler(ILogger<UserCreatedDomainEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(UserCreatedDomainEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "DOMAIN EVENT UserCreated handled. UserId={UserId}, Email={Email}",
            notification.UserId,
            notification.Email);

        return Task.CompletedTask;
    }
}