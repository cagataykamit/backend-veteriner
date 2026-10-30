using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Vaccinations.Contracts.Dtos;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Mediator;

namespace Backend.Veteriner.Application.Vaccinations.Commands.Create;

/// <summary>
/// POST /api/v1/vaccinations. <see cref="ProductId"/> verilirse aynı transaction'da stoktan düşen uygulama da yazılır
/// (<c>CreateAdministrationCommand</c> yeniden kullanılır) ve aşı kaydına bağlanır; <see cref="Quantity"/> (varsayılan 1),
/// <see cref="BatchId"/> (varsayılan FEFO) ve <see cref="RequestId"/> (tekrar koruması) yalnız ürünle birlikte anlamlıdır.
/// </summary>
public sealed record CreateVaccinationCommand(
    Guid ClinicId,
    Guid PetId,
    Guid? ExaminationId,
    Guid VaccineDefinitionId,
    VaccinationStatus Status,
    DateTime? AppliedAtUtc,
    DateTime? DueAtUtc,
    string? Notes,
    Guid? ProductId = null,
    decimal? Quantity = null,
    Guid? BatchId = null,
    Guid? RequestId = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] AdministrationRoute? Route = null,
    string? Site = null)
    : IRequest<Result<VaccinationCreateResultDto>>, ITransactionalRollbackOnFailureRequest, IActivationSignalRequest
{
    public ActivationSignalKind ActivationSignal => ActivationSignalKind.VaccinationCreated;
}
