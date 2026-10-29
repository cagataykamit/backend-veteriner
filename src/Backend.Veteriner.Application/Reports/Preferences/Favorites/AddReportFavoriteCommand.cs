using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Reports;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Reports.Preferences.Favorites;

/// <summary>Raporu oturumdaki kullanıcının favorilerine ekler. Zaten favorideyse değişiklik yapmadan başarılı döner.</summary>
public sealed record AddReportFavoriteCommand(string ReportKey) : IRequest<Result>;

public sealed class AddReportFavoriteCommandValidator : AbstractValidator<AddReportFavoriteCommand>
{
    public AddReportFavoriteCommandValidator()
    {
        RuleFor(x => x.ReportKey).Must(ReportKey.IsValid).WithMessage(ReportKey.InvalidMessage);
    }
}

public sealed class AddReportFavoriteCommandHandler : IRequestHandler<AddReportFavoriteCommand, Result>
{
    public static readonly Error LimitReached = new(
        "ReportFavorites.LimitReached",
        $"En fazla {ReportFavorite.MaxPerUser} rapor favoriye eklenebilir.");

    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IRepository<ReportFavorite> _favorites;
    private readonly TimeProvider _timeProvider;

    public AddReportFavoriteCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IRepository<ReportFavorite> favorites,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _favorites = favorites;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(AddReportFavoriteCommand request, CancellationToken ct)
    {
        var scope = ReportPreferenceScope.Resolve(_tenantContext, _clientContext);
        if (!scope.IsSuccess)
            return Result.Failure(scope.Error);

        var (tenantId, userId) = scope.Value;
        if (await _favorites.AnyAsync(new ReportFavoriteByKeySpec(tenantId, userId, request.ReportKey), ct))
            return Result.Success();

        if (await _favorites.CountAsync(new ReportFavoritesByUserSpec(tenantId, userId), ct) >= ReportFavorite.MaxPerUser)
            return Result.Failure(LimitReached);

        await _favorites.AddAsync(
            ReportFavorite.Create(tenantId, userId, request.ReportKey, _timeProvider.GetUtcNow().UtcDateTime), ct);

        try
        {
            await _favorites.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Aynı anda iki ekleme: benzersiz indeks ikinciyi reddeder; sonuç yine "favoride".
            if (!await _favorites.AnyAsync(new ReportFavoriteByKeySpec(tenantId, userId, request.ReportKey), ct))
                throw;
        }

        return Result.Success();
    }
}
