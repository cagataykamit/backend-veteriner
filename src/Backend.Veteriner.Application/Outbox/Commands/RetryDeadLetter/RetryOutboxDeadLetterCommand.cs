using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Outbox.Commands.RetryDeadLetter;

/// <summary>
/// Yönetim: dead-letter mesajı yeniden denemeye açar (<c>POST /api/v1/admin/outbox/retry/{id}</c>). Platform işlemidir;
/// kiracı abonelik yazma kapısı uygulanmaz.
/// </summary>
public sealed record RetryOutboxDeadLetterCommand(Guid Id) : IRequest<Result>, IIgnoreTenantWriteSubscriptionGuard;

public sealed class RetryOutboxDeadLetterCommandHandler : IRequestHandler<RetryOutboxDeadLetterCommand, Result>
{
    private readonly IOutboxAdminRepository _outbox;
    private readonly IUnitOfWork _uow;
    private readonly IClientContext _client;
    private readonly ILogger<RetryOutboxDeadLetterCommandHandler> _logger;

    public RetryOutboxDeadLetterCommandHandler(
        IOutboxAdminRepository outbox,
        IUnitOfWork uow,
        IClientContext client,
        ILogger<RetryOutboxDeadLetterCommandHandler> logger)
    {
        _outbox = outbox;
        _uow = uow;
        _client = client;
        _logger = logger;
    }

    public async Task<Result> Handle(RetryOutboxDeadLetterCommand request, CancellationToken ct)
    {
        var actorUserId = _client.UserId;

        _logger.LogInformation(
            "AUDIT Outbox.Write RetryDeadLetter actorUserId={ActorUserId} outboxId={OutboxId}",
            actorUserId, request.Id);

        var outcome = await _outbox.RetryDeadLetterAsync(request.Id, ct);

        if (outcome == OutboxRetryOutcome.NotFound)
        {
            _logger.LogInformation(
                "AUDIT Outbox.Write RetryDeadLetter NOT_FOUND actorUserId={ActorUserId} outboxId={OutboxId}",
                actorUserId, request.Id);
            return Result.Failure(OutboxErrors.NotFound);
        }

        if (outcome == OutboxRetryOutcome.NotDeadLetter)
        {
            _logger.LogInformation(
                "AUDIT Outbox.Write RetryDeadLetter INVALID_OPERATION actorUserId={ActorUserId} outboxId={OutboxId} reason=not_dead_letter",
                actorUserId, request.Id);
            return Result.Failure(OutboxErrors.NotDeadLetter);
        }

        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AUDIT Outbox.Write RetryDeadLetter SUCCESS actorUserId={ActorUserId} outboxId={OutboxId}",
            actorUserId, request.Id);

        return Result.Success();
    }
}
