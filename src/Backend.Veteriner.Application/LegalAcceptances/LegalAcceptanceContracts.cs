using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Legal;

namespace Backend.Veteriner.Application.LegalAcceptances;

/// <summary>İstemcinin kabul ettiğini bildirdiği belge ve sürüm (kayıt ve yeniden kabul gövdeleri).</summary>
public sealed record LegalDocumentAcceptanceInput(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] LegalDocumentType DocumentType,
    string? Version);

/// <summary>Belge türü ve güncel sürümü.</summary>
public sealed record LegalDocumentVersionDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] LegalDocumentType DocumentType,
    string Version);

/// <summary>Oturumdaki kullanıcının bekleyen kabulleri; boşsa engel yok.</summary>
public sealed record MyLegalAcceptanceStatusDto(
    bool RequiresAcceptance,
    IReadOnlyList<LegalDocumentVersionDto> PendingDocuments);
