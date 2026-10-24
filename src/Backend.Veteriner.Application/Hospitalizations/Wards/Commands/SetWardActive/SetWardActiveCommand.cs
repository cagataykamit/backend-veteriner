using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Commands.SetWardActive;

/// <summary>
/// Bölümü pasife alır / yeniden etkinleştirir (idempotent). Pasif bölüm yeni yatışa atanamaz; odaları ve mevcut
/// yatışlar değişmez. Yeniden etkinleştirmede ad benzersizliği tekrar denetlenir.
/// </summary>
public sealed record SetWardActiveCommand(Guid ClinicId, Guid WardId, bool IsActive)
    : IRequest<Result<HospitalizationWardDto>>;
