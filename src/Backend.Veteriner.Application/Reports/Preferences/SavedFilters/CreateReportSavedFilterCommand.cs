using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Reports;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Reports.Preferences.SavedFilters;

/// <summary>Bir rapordaki filtre setini oturumdaki kullanıcı adına adla kaydeder.</summary>
public sealed record CreateReportSavedFilterCommand(string ReportKey, string Name, IReadOnlyDictionary<string, string> Filters)
    : IRequest<Result<ReportSavedFilterDto>>;

public sealed class CreateReportSavedFilterCommandValidator : AbstractValidator<CreateReportSavedFilterCommand>
{
    public CreateReportSavedFilterCommandValidator()
    {
        RuleFor(x => x.ReportKey).Must(ReportKey.IsValid).WithMessage(ReportKey.InvalidMessage);
        RuleFor(x => x.Name).Custom((name, ctx) =>
        {
            var check = ReportSavedFilter.ValidateName(name);
            if (!check.IsSuccess)
                ctx.AddFailure(check.Error.Message);
        });
        RuleFor(x => x.Filters).Custom((filters, ctx) =>
        {
            var check = ReportSavedFilter.ValidateFilters(filters);
            if (!check.IsSuccess)
                ctx.AddFailure(check.Error.Message);
        });
    }
}

public sealed class CreateReportSavedFilterCommandHandler
    : IRequestHandler<CreateReportSavedFilterCommand, Result<ReportSavedFilterDto>>
{
    public static readonly Error DuplicateName = new(
        "ReportSavedFilters.DuplicateName", "Bu raporda aynı adla kayıtlı bir filtre var; başka bir ad seçin.");

    public static readonly Error LimitReached = new(
        "ReportSavedFilters.LimitReached",
        $"Bir rapor için en fazla {ReportSavedFilter.MaxPerReport} filtre kaydedilebilir; kullanmadığınızı silin.");

    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IRepository<ReportSavedFilter> _filters;
    private readonly TimeProvider _timeProvider;

    public CreateReportSavedFilterCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IRepository<ReportSavedFilter> filters,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _filters = filters;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ReportSavedFilterDto>> Handle(CreateReportSavedFilterCommand request, CancellationToken ct)
    {
        var scope = ReportPreferenceScope.Resolve(_tenantContext, _clientContext);
        if (!scope.IsSuccess)
            return Result<ReportSavedFilterDto>.Failure(scope.Error);

        var (tenantId, userId) = scope.Value;
        var entity = ReportSavedFilter.Create(
            tenantId, userId, request.ReportKey, request.Name, request.Filters, _timeProvider.GetUtcNow().UtcDateTime);

        if (await _filters.AnyAsync(new ReportSavedFilterByNameSpec(tenantId, userId, entity.ReportKey, entity.NormalizedName), ct))
            return Result<ReportSavedFilterDto>.Failure(DuplicateName);

        if (await _filters.CountAsync(new ReportSavedFiltersByReportSpec(tenantId, userId, entity.ReportKey), ct)
            >= ReportSavedFilter.MaxPerReport)
        {
            return Result<ReportSavedFilterDto>.Failure(LimitReached);
        }

        await _filters.AddAsync(entity, ct);
        try
        {
            await _filters.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Aynı adla eşzamanlı kayıt: benzersiz indeks ikinciyi reddeder.
            if (await _filters.AnyAsync(new ReportSavedFilterByNameSpec(tenantId, userId, entity.ReportKey, entity.NormalizedName), ct))
                return Result<ReportSavedFilterDto>.Failure(DuplicateName);
            throw;
        }

        return Result<ReportSavedFilterDto>.Success(ReportSavedFilterDto.From(entity));
    }
}
