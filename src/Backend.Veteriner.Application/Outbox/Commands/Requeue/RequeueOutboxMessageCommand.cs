using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Outbox.Commands.Requeue;

/// <summary>
/// Mesajı sıfırdan kuyruğa alır (<c>POST /api/v1/outbox/requeue/{id}</c>). Platform işlemidir; kiracı abonelik yazma
/// kapısı uygulanmaz.
/// </summary>
public sealed record RequeueOutboxMessageCommand(Guid Id) : IRequest<Result>, IIgnoreTenantWriteSubscriptionGuard;

public sealed class RequeueOutboxMessageCommandHandler : IRequestHandler<RequeueOutboxMessageCommand, Result>
{
    private readonly IOutboxAdminRepository _outbox;
    private readonly IUnitOfWork _uow;

    public RequeueOutboxMessageCommandHandler(IOutboxAdminRepository outbox, IUnitOfWork uow)
    {
        _outbox = outbox;
        _uow = uow;
    }

    public async Task<Result> Handle(RequeueOutboxMessageCommand request, CancellationToken ct)
    {
        if (!await _outbox.RequeueAsync(request.Id, ct))
            return Result.Failure(OutboxErrors.NotFound);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
