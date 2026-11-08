using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Imports.Queries.GetSourceProfiles;

/// <summary>Hazır eşlemenin bir satırı; <see cref="Table"/> tablo anahtarı (<c>customers|pets|vaccinations</c>).</summary>
public sealed record ImportProfileMappingDto(string Table, string Header, string Field, string Source, bool Verified);

/// <summary>Kaynak program profili; kullanıcı yükleme öncesi seçer, <see cref="Mappings"/> sütun önerisini besler.</summary>
public sealed record ImportSourceProfileDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ImportSourceProfile Code,
    string DisplayName,
    string Note,
    bool Verified,
    IReadOnlyList<ImportProfileMappingDto> Mappings);

/// <summary>GET /api/v1/imports/source-profiles: "Hangi programdan geliyorsunuz?" seçenekleri (TIER-021). Her pakette açık.</summary>
public sealed record GetImportSourceProfilesQuery : IRequest<Result<IReadOnlyList<ImportSourceProfileDto>>>;

public sealed class GetImportSourceProfilesQueryHandler
    : IRequestHandler<GetImportSourceProfilesQuery, Result<IReadOnlyList<ImportSourceProfileDto>>>
{
    public ValueTask<Result<IReadOnlyList<ImportSourceProfileDto>>> Handle(GetImportSourceProfilesQuery request, CancellationToken ct)
    {
        IReadOnlyList<ImportSourceProfileDto> profiles = ImportSourceProfiles.All
            .Select(p => new ImportSourceProfileDto(
                p.Code,
                p.DisplayName,
                p.Note,
                p.Verified,
                p.Mappings
                    .Select(m => new ImportProfileMappingDto(ImportCanonicalNames.Key(m.Table), m.Header, m.Field, m.Source, m.Verified))
                    .ToList()))
            .ToList();

        return ValueTask.FromResult(Result<IReadOnlyList<ImportSourceProfileDto>>.Success(profiles));
    }
}
