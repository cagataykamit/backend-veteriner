using Backend.Veteriner.Application.Common.Abstractions;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Outbox.Commands.RetryAllDeadLetters;

/// <summary>
/// Yönetim: işlenmemiş tüm dead-letter mesajları yeniden denemeye açar ve sayısını döner
/// (<c>POST /api/v1/admin/outbox/retry-dead-all</c>). Platform işlemidir; kiracı abonelik yazma kapısı uygulanmaz.
/// </summary>
public sealed record RetryAllOutboxDeadLettersCommand : IRequest<int>, IIgnoreTenantWriteSubscriptionGuard;

public sealed class RetryAllOutboxDeadLettersCommandHandler : IRequestHandler<RetryAllOutboxDeadLettersCommand, int>
{
    private readonly IOutboxAdminRepository _outbox;
    private readonly IUnitOfWork _uow;
    private readonly IClientContext _client;
    private readonly ILogger<RetryAllOutboxDeadLettersCommandHandler> _logger;

    public RetryAllOutboxDeadLettersCommandHandler(
        IOutboxAdminRepository outbox,
        IUnitOfWork uow,
        IClientContext client,
        ILogger<RetryAllOutboxDeadLettersCommandHandler> logger)
    {
        _outbox = outbox;
        _uow = uow;
        _client = client;
        _logger = logger;
    }

    public async ValueTask<int> Handle(RetryAllOutboxDeadLettersCommand request, CancellationToken ct)
    {
        var actorUserId = _client.UserId;

        _logger.LogInformation(
            "AUDIT Outbox.Write RetryAllDeadLetter actorUserId={ActorUserId}",
            actorUserId);

        var count = await _outbox.RetryAllDeadLettersAsync(ct);

        if (count == 0)
        {
            _logger.LogInformation(
                "AUDIT Outbox.Write RetryAllDeadLetter SUCCESS actorUserId={ActorUserId} count=0",
                actorUserId);

            return 0;
        }

        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AUDIT Outbox.Write RetryAllDeadLetter SUCCESS actorUserId={ActorUserId} count={Count}",
            actorUserId, count);

        return count;
    }
}
