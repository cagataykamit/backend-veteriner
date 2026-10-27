using Backend.Veteriner.Application.Common.Behaviors;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Mediator;

namespace Backend.Veteriner.Infrastructure.Behaviors;

/// <summary>
/// Repository yazımları kaydetmez; commit handler'daki <c>SaveChangesAsync</c> çağrısıdır (ADR-039).
/// Bu katman kaydı unutulmuş değişikliği yakalar: en dış istek başarılı döndüğünde değişiklik izleyicisinde
/// ya da outbox tamponunda kaydedilmemiş değişiklik kalmışsa istisna fırlatır (sessiz veri kaybı yerine 500).
///
/// Notlar:
/// - Başarısız <c>Result</c> dönen istekte bekleyen değişiklikler bilinçli olarak atılır (kaydedilmez).
/// - İç içe <c>Send</c>'de yalnız en dış istek denetlenir; dıştaki handler iç komuttan sonra kaydedebilir.
/// - Transaction katmanının içinde koşar; istisna açık transaction'ı da geri aldırır.
/// </summary>
public sealed class UnsavedChangesGuardBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, IMessage
{
    private readonly AppDbContext _db;
    private readonly OutboxBuffer _outboxBuffer;

    public UnsavedChangesGuardBehavior(AppDbContext db, OutboxBuffer outboxBuffer)
    {
        _db = db;
        _outboxBuffer = outboxBuffer;
    }

    public async ValueTask<TResponse> Handle(
        TRequest request,
        MessageHandlerDelegate<TRequest, TResponse> next,
        CancellationToken ct)
    {
        if (UnsavedChangesGuardScope.Active.Value)
            return await next(request, ct);

        UnsavedChangesGuardScope.Active.Value = true;

        var response = await next(request, ct);

        if (ResultOutcome.Of(response).Success && (_db.ChangeTracker.HasChanges() || _outboxBuffer.PendingCount > 0))
        {
            throw new InvalidOperationException(
                $"{typeof(TRequest).Name} başarılı döndü ama kaydedilmemiş değişiklik bıraktı; handler SaveChangesAsync çağırmalı (ADR-039).");
        }

        return response;
    }
}

/// <summary>
/// İç içe isteği ayırt etmek için akış (async) başına işaret. Generic sınıfta statik alan kapalı tip başına
/// ayrı olacağından ayrı tutulur; async metotta verilen değer çağırana geri akmaz, yalnız iç çağrılara akar.
/// </summary>
internal static class UnsavedChangesGuardScope
{
    internal static readonly AsyncLocal<bool> Active = new();
}
