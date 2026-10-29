using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Reports;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Reports.Preferences.SavedFilters;

/// <summary>Oturumdaki kullanıcının kayıtlı filtresini siler; başka kullanıcının kaydı bulunamaz sayılır (404).</summary>
public sealed record DeleteReportSavedFilterCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteReportSavedFilterCommandHandler : IRequestHandler<DeleteReportSavedFilterCommand, Result>
{
    public static readonly Error NotFound = new("ReportSavedFilters.NotFound", "Kayıtlı filtre bulunamadı.");

    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IRepository<ReportSavedFilter> _filters;

    public DeleteReportSavedFilterCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IRepository<ReportSavedFilter> filters)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _filters = filters;
    }

    public async ValueTask<Result> Handle(DeleteReportSavedFilterCommand request, CancellationToken ct)
    {
        var scope = ReportPreferenceScope.Resolve(_tenantContext, _clientContext);
        if (!scope.IsSuccess)
            return Result.Failure(scope.Error);

        var (tenantId, userId) = scope.Value;
        var entity = await _filters.FirstOrDefaultAsync(new ReportSavedFilterByIdSpec(tenantId, userId, request.Id), ct);
        if (entity is null)
            return Result.Failure(NotFound);

        await _filters.DeleteAsync(entity, ct);
        await _filters.SaveChangesAsync(ct);
        return Result.Success();
    }
}
