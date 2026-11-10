using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.EDocuments.Contracts;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.EDocuments.Commands.Actions;

public sealed class RetryEDocumentCommandHandler : IRequestHandler<RetryEDocumentCommand, Result<EDocumentDto>>
{
    private readonly EDocumentActionRunner _runner;
    private readonly EDocumentSender _sender;
    private readonly TimeProvider _timeProvider;

    public RetryEDocumentCommandHandler(EDocumentActionRunner runner, EDocumentSender sender, TimeProvider timeProvider)
    {
        _runner = runner;
        _sender = sender;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<EDocumentDto>> Handle(RetryEDocumentCommand request, CancellationToken ct)
        => await _runner.RunAsync(request.Id, PermissionCatalog.EDocuments.Issue, async (document, connection, _, token) =>
        {
            if (document.Status != EDocumentStatus.Failed)
                return Result.Failure("EDocuments.InvalidState", "Yalnız hatalı belge yeniden denenebilir.");
            if (connection is null || !connection.IsUsable)
                return Result.Failure(EDocumentProviderGateway.NotConnected(connection));

            var begun = document.BeginSending(_timeProvider.GetUtcNow().UtcDateTime);
            if (!begun.IsSuccess)
                return begun;
            await _sender.SendAsync(document, connection, token);
            return Result.Success();
        }, ct);
}

public sealed class DiscardEDocumentCommandHandler : IRequestHandler<DiscardEDocumentCommand, Result<EDocumentDto>>
{
    private readonly EDocumentActionRunner _runner;
    private readonly EDocumentSender _sender;
    private readonly TimeProvider _timeProvider;

    public DiscardEDocumentCommandHandler(EDocumentActionRunner runner, EDocumentSender sender, TimeProvider timeProvider)
    {
        _runner = runner;
        _sender = sender;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<EDocumentDto>> Handle(DiscardEDocumentCommand request, CancellationToken ct)
        => await _runner.RunAsync(request.Id, PermissionCatalog.EDocuments.Issue, async (document, connection, caller, token) =>
        {
            var discarded = document.Discard(caller.UserId, request.Reason, _timeProvider.GetUtcNow().UtcDateTime);
            if (!discarded.IsSuccess)
                return discarded;

            await _sender.DiscardDraftAsync(document, connection, token);
            await _sender.ReleaseChargesAsync(document, token);
            return Result.Success();
        }, ct);
}

public sealed class CancelEDocumentCommandHandler : IRequestHandler<CancelEDocumentCommand, Result<EDocumentDto>>
{
    private readonly EDocumentActionRunner _runner;
    private readonly EDocumentSender _sender;
    private readonly TimeProvider _timeProvider;

    public CancelEDocumentCommandHandler(EDocumentActionRunner runner, EDocumentSender sender, TimeProvider timeProvider)
    {
        _runner = runner;
        _sender = sender;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<EDocumentDto>> Handle(CancelEDocumentCommand request, CancellationToken ct)
        => await _runner.RunAsync(request.Id, PermissionCatalog.EDocuments.Cancel, async (document, connection, caller, token) =>
        {
            if (connection is null || !connection.IsUsable)
                return Result.Failure(EDocumentProviderGateway.NotConnected(connection));

            var requested = document.RequestCancel(caller.UserId, request.Reason, _timeProvider.GetUtcNow().UtcDateTime);
            if (!requested.IsSuccess)
                return requested;

            await _sender.CancelAsync(document, connection, token);
            return Result.Success();
        }, ct);
}

public sealed class RefreshEDocumentCommandHandler : IRequestHandler<RefreshEDocumentCommand, Result<EDocumentDto>>
{
    private readonly EDocumentActionRunner _runner;
    private readonly EDocumentSender _sender;
    private readonly EDocumentProcessorOptions _options;

    public RefreshEDocumentCommandHandler(EDocumentActionRunner runner, EDocumentSender sender, IOptions<EDocumentProcessorOptions> options)
    {
        _runner = runner;
        _sender = sender;
        _options = options.Value;
    }

    public async ValueTask<Result<EDocumentDto>> Handle(RefreshEDocumentCommand request, CancellationToken ct)
        => await _runner.RunAsync(request.Id, PermissionCatalog.EDocuments.Read, async (document, connection, _, token) =>
        {
            await _sender.CheckAsync(document, connection, _options.MaxAttempts, token);
            return Result.Success();
        }, ct);
}
