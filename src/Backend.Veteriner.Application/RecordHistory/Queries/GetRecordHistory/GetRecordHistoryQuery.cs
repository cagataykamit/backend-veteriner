using Backend.Veteriner.Application.RecordHistory.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.RecordHistory.Queries.GetRecordHistory;

/// <summary>
/// Klinik kaydın değişiklik geçmişi (RECORD-002). <see cref="RecordType"/> bir <c>RecordHistoryTypes</c> değeridir;
/// okuma yetkisi kayıt türünün kendi okuma iznidir (uçta).
/// </summary>
public sealed record GetRecordHistoryQuery(string RecordType, Guid RecordId)
    : IRequest<Result<IReadOnlyList<RecordChangeDto>>>;
