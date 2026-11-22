using Backend.Veteriner.Application.LabResults.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.LabResults.Queries.GetParameterHistory;

/// <summary>
/// Hastada bir parametrenin sonuçlanmış lab kayıtlarındaki değerleri, yeniden eskiye (REF-001 Dilim 3 eğilim listesi).
/// Parametre adı yazım farkından bağımsız eşlenir; birim süzülmez, her noktada birim döner.
/// </summary>
public sealed record GetLabParameterHistoryQuery(Guid PetId, string Parameter, Guid? ClinicId = null)
    : IRequest<Result<IReadOnlyList<LabParameterHistoryPointDto>>>;
