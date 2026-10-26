using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Outbox.Commands.Delete;

/// <summary>
/// Yönetim: outbox mesajını siler (<c>DELETE /api/v1/admin/outbox/{id}</c>). Platform işlemidir; kiracı abonelik yazma
/// kapısı uygulanmaz.
/// </summary>
public sealed record DeleteOutboxMessageCommand(Guid Id) : IRequest<Result>, IIgnoreTenantWriteSubscriptionGuard;

public sealed class DeleteOutboxMessageCommandHandler : IRequestHandler<DeleteOutboxMessageCommand, Result>
{
    private readonly IOutboxAdminRepository _outbox;
    private readonly IUnitOfWork _uow;
    private readonly IClientContext _client;
    private readonly ILogger<DeleteOutboxMessageCommandHandler> _logger;

    public DeleteOutboxMessageCommandHandler(
        IOutboxAdminRepository outbox,
        IUnitOfWork uow,
        IClientContext client,
        ILogger<DeleteOutboxMessageCommandHandler> logger)
    {
        _outbox = outbox;
        _uow = uow;
        _client = client;
        _logger = logger;
    }

    public async ValueTask<Result> Handle(DeleteOutboxMessageCommand request, CancellationToken ct)
    {
        var actorUserId = _client.UserId;

        _logger.LogInformation(
            "AUDIT Outbox.Write DeleteOutboxMessage actorUserId={ActorUserId} outboxId={OutboxId}",
            actorUserId, request.Id);

        if (!await _outbox.RemoveAsync(request.Id, ct))
        {
            _logger.LogInformation(
                "AUDIT Outbox.Write DeleteOutboxMessage NOT_FOUND actorUserId={ActorUserId} outboxId={OutboxId}",
                actorUserId, request.Id);

            return Result.Failure(OutboxErrors.NotFound);
        }

        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AUDIT Outbox.Write DeleteOutboxMessage SUCCESS actorUserId={ActorUserId} outboxId={OutboxId}",
            actorUserId, request.Id);

        return Result.Success();
    }
}
