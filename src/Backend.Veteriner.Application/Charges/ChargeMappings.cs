using Backend.Veteriner.Application.Charges.Contracts.Dtos;
using Backend.Veteriner.Domain.Charges;

namespace Backend.Veteriner.Application.Charges;

internal static class ChargeMappings
{
    public static ChargeDto ToDto(this Charge c, string clientName, string? petName)
        => new(
            c.Id,
            c.ClinicId,
            c.ClientId,
            clientName,
            c.PetId,
            petName,
            c.VisitId,
            c.ExaminationId,
            c.Description,
            c.Quantity,
            c.UnitPrice,
            c.Amount,
            c.Currency,
            c.CreatedByUserId,
            c.CreatedAtUtc,
            c.AllocatedAmount,
            c.OutstandingAmount);
}
