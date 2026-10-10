using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Visits.Queries.GetToday;

/// <param name="ClinicId">Klinik bağlamı yoksa zorunlu.</param>
/// <param name="LocalDate">İstanbul takvim günü; verilmezse bugün.</param>
/// <param name="Voided">true ise yalnızca o günün yanlış geliş işaretli gelişleri döner.</param>
/// <param name="ResponsibleVeterinarianUserId">Verilirse yalnızca sorumlu hekimi bu kullanıcı olan gelişler döner (aktif klinik hekimi olmalı).</param>
public sealed record GetVisitsTodayQuery(Guid? ClinicId, DateOnly? LocalDate, bool Voided = false, Guid? ResponsibleVeterinarianUserId = null)
    : IRequest<Result<TodayDto>>;
