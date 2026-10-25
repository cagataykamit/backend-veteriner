using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Activation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Common.Behaviors;

/// <summary>
/// Deneme aktivasyon ölçümü (TRIAL-008): <see cref="IActivationSignalRequest"/> işaretli komut başarıyla bitince kiracının
/// ilk anlamlı aksiyonunu kaydeder. Transaction davranışının dışında koşar (komut commit'inden sonra); kaydedici hatası
/// yutulur ve loglanır — ölçüm iş akışını asla bozmaz (denetim kaydıyla aynı ilke). Kiracı bağlamı yoksa (public akışlar)
/// yazmaz.
/// </summary>
public sealed class ActivationSignalBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _client;
    private readonly ITenantActivationRecorder _recorder;
    private readonly ILogger<ActivationSignalBehavior<TRequest, TResponse>> _logger;

    public ActivationSignalBehavior(
        ITenantContext tenantContext,
        IClientContext client,
        ITenantActivationRecorder recorder,
        ILogger<ActivationSignalBehavior<TRequest, TResponse>> logger)
    {
        _tenantContext = tenantContext;
        _client = client;
        _recorder = recorder;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not IActivationSignalRequest signal)
            return await next();

        var response = await next();

        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
            return response;

        var (success, _) = ResultOutcome.Of(response);
        if (!success)
            return response;

        try
        {
            await _recorder.RecordFirstActionAsync(tenantId.Value, signal.ActivationSignal, _client.UserId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Aktivasyon işareti yazılamadı; iş sonucu etkilenmedi. TenantId={TenantId} Kind={Kind} Request={Request}",
                tenantId.Value, signal.ActivationSignal, typeof(TRequest).Name);
        }

        return response;
    }
}
