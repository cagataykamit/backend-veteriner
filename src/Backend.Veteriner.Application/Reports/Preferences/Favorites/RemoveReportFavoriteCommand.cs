using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Reports;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Reports.Preferences.Favorites;

/// <summary>Raporu oturumdaki kullanıcının favorilerinden çıkarır. Favoride değilse değişiklik yapmadan başarılı döner.</summary>
public sealed record RemoveReportFavoriteCommand(string ReportKey) : IRequest<Result>;

public sealed class RemoveReportFavoriteCommandValidator : AbstractValidator<RemoveReportFavoriteCommand>
{
    public RemoveReportFavoriteCommandValidator()
    {
        RuleFor(x => x.ReportKey).Must(ReportKey.IsValid).WithMessage(ReportKey.InvalidMessage);
    }
}

public sealed class RemoveReportFavoriteCommandHandler : IRequestHandler<RemoveReportFavoriteCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IRepository<ReportFavorite> _favorites;

    public RemoveReportFavoriteCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IRepository<ReportFavorite> favorites)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _favorites = favorites;
    }

    public async ValueTask<Result> Handle(RemoveReportFavoriteCommand request, CancellationToken ct)
    {
        var scope = ReportPreferenceScope.Resolve(_tenantContext, _clientContext);
        if (!scope.IsSuccess)
            return Result.Failure(scope.Error);

        var (tenantId, userId) = scope.Value;
        var favorite = await _favorites.FirstOrDefaultAsync(new ReportFavoriteByKeySpec(tenantId, userId, request.ReportKey), ct);
        if (favorite is null)
            return Result.Success();

        await _favorites.DeleteAsync(favorite, ct);
        await _favorites.SaveChangesAsync(ct);
        return Result.Success();
    }
}
