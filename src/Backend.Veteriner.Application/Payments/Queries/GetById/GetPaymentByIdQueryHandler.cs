using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Payments.ReadModels;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Payments.Queries.GetById;

public sealed class GetPaymentByIdQueryHandler
    : IRequestHandler<GetPaymentByIdQuery, Result<PaymentDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IUserOperationClaimRepository _userOperationClaims;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<Payment> _payments;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly IPaymentGetByIdReadModelReader _getByIdReadModelReader;
    private readonly QueryReadModelsOptions _queryReadModelsOptions;
    private readonly ILogger<GetPaymentByIdQueryHandler>? _logger;

    public GetPaymentByIdQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IUserOperationClaimRepository userOperationClaims,
        IUserClinicRepository userClinics,
        IReadRepository<Payment> payments,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        IPaymentGetByIdReadModelReader getByIdReadModelReader,
        IOptions<QueryReadModelsOptions> queryReadModelsOptions,
        ILogger<GetPaymentByIdQueryHandler>? logger = null)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _userOperationClaims = userOperationClaims;
        _userClinics = userClinics;
        _payments = payments;
        _pets = pets;
        _clients = clients;
        _getByIdReadModelReader = getByIdReadModelReader;
        _queryReadModelsOptions = queryReadModelsOptions.Value;
        _logger = logger;
    }

    public async Task<Result<PaymentDetailDto>> Handle(GetPaymentByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PaymentDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<PaymentDetailDto>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        if (_queryReadModelsOptions.PaymentsGetByIdReadEnabled)
            return await HandleQueryPathAsync(tenantId, userId, request.Id, ct);

        return await HandleCommandPathAsync(tenantId, userId, request.Id, ct);
    }

    private async Task<Result<PaymentDetailDto>> HandleQueryPathAsync(
        Guid tenantId,
        Guid userId,
        Guid paymentId,
        CancellationToken ct)
    {
        var dto = await _getByIdReadModelReader.GetByIdAsync(tenantId, paymentId, ct);
        if (dto is null)
            return Result<PaymentDetailDto>.Failure("Payments.NotFound", "Ödeme kaydı bulunamadı.");

        var authFailure = await TryGetClinicAccessFailureAsync(userId, dto.ClinicId, ct);
        if (authFailure is not null)
            return authFailure.Value;

        _logger?.LogInformation(
            "Payment detail generated from Query DB read model. TenantId={TenantId} PaymentId={PaymentId}",
            tenantId,
            paymentId);

        return Result<PaymentDetailDto>.Success(dto);
    }

    private async Task<Result<PaymentDetailDto>> HandleCommandPathAsync(
        Guid tenantId,
        Guid userId,
        Guid paymentId,
        CancellationToken ct)
    {
        var p = await _payments.FirstOrDefaultAsync(
            new PaymentByIdSpec(tenantId, paymentId), ct);
        if (p is null)
            return Result<PaymentDetailDto>.Failure("Payments.NotFound", "Ödeme kaydı bulunamadı.");

        var authFailure = await TryGetClinicAccessFailureAsync(userId, p.ClinicId, ct);
        if (authFailure is not null)
            return authFailure.Value;

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, p.ClientId), ct);
        var clientName = client?.FullName ?? string.Empty;

        string petName = string.Empty;
        if (p.PetId is { } petId)
        {
            var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, petId), ct);
            petName = pet?.Name ?? string.Empty;
        }

        var dto = new PaymentDetailDto(
            p.Id,
            p.TenantId,
            p.ClinicId,
            p.ClientId,
            clientName,
            p.PetId,
            petName,
            p.AppointmentId,
            p.ExaminationId,
            p.Amount,
            p.Currency,
            p.Method,
            p.PaidAtUtc,
            p.Notes);
        return Result<PaymentDetailDto>.Success(dto);
    }

    private async Task<Result<PaymentDetailDto>?> TryGetClinicAccessFailureAsync(
        Guid userId,
        Guid paymentClinicId,
        CancellationToken ct)
    {
        if (_clinicContext.ClinicId is { } clinicId && paymentClinicId != clinicId)
            return Result<PaymentDetailDto>.Failure("Payments.NotFound", "Ödeme kaydı bulunamadı.");

        var operationClaimNames = await _userOperationClaims.GetOperationClaimNamesByUserIdAsync(userId, ct);
        if (!TenantWideClaimNames.IsTenantWide(operationClaimNames))
        {
            if (!await _userClinics.ExistsAsync(userId, paymentClinicId, ct))
            {
                return Result<PaymentDetailDto>.Failure(
                    "Payments.NotFound",
                    "Ödeme kaydı bulunamadı.");
            }
        }

        return null;
    }
}
