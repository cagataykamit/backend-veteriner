using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Commands.QuickRegister;

/// <summary>
/// Randevusuz gelişte müşteri + hayvan tek işlemde. Telefon zorunludur. Alan kuralları mevcut
/// <c>CreateClientCommand</c> / <c>CreatePetCommand</c> doğrulayıcı ve handler'larından gelir; burada yeniden yazılmaz.
/// Başarısız sonuçta işlem geri alınır (yarım kayıt kalmaz).
/// </summary>
public sealed record QuickRegisterClientCommand(
    string FullName,
    string Phone,
    string PetName,
    Guid SpeciesId,
    Guid? BreedId = null,
    string? Breed = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetGender? Gender = null,
    DateOnly? BirthDate = null,
    string? MicrochipNumber = null,
    bool IsNeutered = false)
    : IRequest<Result<QuickRegisterClientResultDto>>, ITransactionalRollbackOnFailureRequest;
