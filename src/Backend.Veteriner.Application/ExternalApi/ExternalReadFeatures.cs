using System.Buffers.Text;
using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json.Serialization;
using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using Mediator;

namespace Backend.Veteriner.Application.ExternalApi;

// Dış API v1 okuma uçları (TIER-009; docs/EXTERNAL_API_CONTRACT.md §4–§5). Alanlar KVKK asgari veri ilkesiyle tek tek seçilir; varlık
// doğrudan dışarı verilmez. Yeni alan sözleşmedeki tabloya yazılmadan eklenmez.

/// <summary>Sayfadaki her kayıt: imleç <see cref="ChangedAtUtc"/> ve <see cref="Id"/> sırasından üretilir.</summary>
public interface IExternalRecord
{
    Guid Id { get; }
    DateTime ChangedAtUtc { get; }
}

/// <summary><see cref="NextCursor"/> null ise son sayfa; değilse aynı filtreyle <c>cursor</c> olarak gönderilir.</summary>
public sealed record ExternalPage<T>(IReadOnlyList<T> Items, int PageSize, string? NextCursor);

public sealed record ExternalClientDto(
    Guid Id,
    string FullName,
    string? Phone,
    string? Email,
    DateTime CreatedAtUtc,
    DateTime ChangedAtUtc) : IExternalRecord;

public sealed record ExternalPetDto(
    Guid Id,
    Guid ClientId,
    string Name,
    string? Species,
    string? Breed,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetGender? Gender,
    DateOnly? BirthDate,
    bool IsNeutered,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetStatus Status,
    DateTime ChangedAtUtc) : IExternalRecord;

public sealed record ExternalAppointmentDto(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    DateTime ScheduledAtUtc,
    int DurationMinutes,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] AppointmentType AppointmentType,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] AppointmentStatus Status,
    DateTime ChangedAtUtc) : IExternalRecord;

public sealed record ExternalVaccinationDto(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    string VaccineName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] VaccinationStatus Status,
    DateTime? AppliedAtUtc,
    DateTime? DueAtUtc,
    DateTime ChangedAtUtc) : IExternalRecord;

/// <summary>Ortak sayfa isteği: <c>updatedSince</c> (dahil, UTC), opak <c>cursor</c> ve <c>pageSize</c>.</summary>
public sealed record ExternalPageRequest(DateTime? UpdatedSince, string? Cursor, int? PageSize);

public sealed record GetExternalClientsQuery(ExternalPageRequest Page) : IRequest<Result<ExternalPage<ExternalClientDto>>>;
public sealed record GetExternalPetsQuery(ExternalPageRequest Page) : IRequest<Result<ExternalPage<ExternalPetDto>>>;
public sealed record GetExternalAppointmentsQuery(ExternalPageRequest Page) : IRequest<Result<ExternalPage<ExternalAppointmentDto>>>;
public sealed record GetExternalVaccinationsQuery(ExternalPageRequest Page) : IRequest<Result<ExternalPage<ExternalVaccinationDto>>>;

/// <summary>Sayfa ve imleç kuralı — tek yer. Sıra (<c>ChangedAtUtc</c>, <c>Id</c>); imleç son kaydın bu ikilisidir.</summary>
public static class ExternalPaging
{
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 200;

    public sealed record Position(DateTime ChangedAtUtc, Guid Id);

    public sealed record Resolved(Guid TenantId, DateTime? UpdatedSince, Position? After, int PageSize);

    public static Result<Resolved> Resolve(ITenantContext tenantContext, ExternalPageRequest request)
    {
        // Kiracı yalnız anahtardan gelir (ExternalApiAccess.UseTenant); yoksa istek buraya ulaşmamalıdır.
        if (tenantContext.TenantId is not { } tenantId)
            return Result<Resolved>.Failure(ExternalApiErrors.KeyInvalid);

        Position? after = null;
        if (!string.IsNullOrEmpty(request.Cursor))
        {
            if (Decode(request.Cursor) is not { } decoded)
                return Result<Resolved>.Failure(ExternalApiErrors.ValidationCode, "cursor geçersiz; önceki yanıttaki nextCursor aynen gönderilmelidir.");
            after = decoded;
        }

        DateTime? since = request.UpdatedSince is { } s
            ? s.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(s, DateTimeKind.Utc) : s.ToUniversalTime()
            : null;
        var pageSize = Math.Clamp(request.PageSize ?? DefaultPageSize, 1, MaxPageSize);
        return Result<Resolved>.Success(new Resolved(tenantId, since, after, pageSize));
    }

    /// <summary>Sayfa için <c>pageSize + 1</c> kayıt istenir; fazlası sonraki sayfanın varlığını gösterir.</summary>
    public static int Take(Resolved page) => page.PageSize + 1;

    public static ExternalPage<T> ToPage<T>(IReadOnlyList<T> rows, Resolved page) where T : IExternalRecord
    {
        var items = rows.Take(page.PageSize).ToList();
        var next = rows.Count > page.PageSize ? Encode(new Position(items[^1].ChangedAtUtc, items[^1].Id)) : null;
        return new ExternalPage<T>(items, page.PageSize, next);
    }

    /// <summary>Kiracı, <c>updatedSince</c> ve imleç koşulu ile sıra; ADR-040 global filtresine ek ikinci savunma hattı.</summary>
    public static void Apply<T>(ISpecificationBuilder<T> query, Resolved page) where T : class, ITenantOwned, IChangeTracked
    {
        query.AsNoTracking();
        query.Where(x => x.TenantId == page.TenantId);
        if (page.UpdatedSince is { } since)
            query.Where(x => x.ChangedAtUtc >= since);
        if (page.After is { } after)
            query.Where(After<T>(after));
        query.OrderBy(x => x.ChangedAtUtc).ThenBy(x => x.Id);
        query.Take(Take(page));
    }

    // Guid karşılaştırması SQL Server uniqueidentifier sırasıyla yapılır; ORDER BY da aynı sırayı kullandığı için imleç tutarlıdır.
    private static Expression<Func<T, bool>> After<T>(Position after) where T : class, IChangeTracked
        => x => x.ChangedAtUtc > after.ChangedAtUtc
                || (x.ChangedAtUtc == after.ChangedAtUtc && x.Id.CompareTo(after.Id) > 0);

    private static string Encode(Position p)
        => Base64Url.EncodeToString(Encoding.ASCII.GetBytes($"{p.ChangedAtUtc.Ticks.ToString(CultureInfo.InvariantCulture)}.{p.Id:N}"));

    private static Position? Decode(string cursor)
    {
        try
        {
            var text = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(cursor));
            var parts = text.Split('.');
            if (parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks is >= 0 and <= 3155378975999999999
                && Guid.TryParseExact(parts[1], "N", out var id))
                return new Position(new DateTime(ticks, DateTimeKind.Utc), id);
        }
        catch (FormatException)
        {
        }

        return null;
    }
}

/// <summary>Dört liste ucunun ortak akışı: sayfa isteğini çöz, <c>pageSize + 1</c> oku, sayfayı ve sonraki imleci üret.</summary>
internal static class ExternalListRunner
{
    public static async ValueTask<Result<ExternalPage<TDto>>> ListAsync<T, TDto>(
        ITenantContext tenantContext,
        IReadRepository<T> repository,
        ExternalPageRequest request,
        Func<ExternalPaging.Resolved, ISpecification<T, TDto>> spec,
        CancellationToken ct)
        where T : class
        where TDto : IExternalRecord
    {
        var page = ExternalPaging.Resolve(tenantContext, request);
        if (!page.IsSuccess)
            return Result<ExternalPage<TDto>>.Failure(page.Error);

        var rows = await repository.ListAsync(spec(page.Value!), ct);
        return Result<ExternalPage<TDto>>.Success(ExternalPaging.ToPage(rows, page.Value!));
    }
}

/// <summary>Peşin (kayıtsız) satış müşterisi gerçek kişi değildir, listelenmez.</summary>
public sealed class ExternalClientsPageSpec : Specification<Client, ExternalClientDto>
{
    public ExternalClientsPageSpec(ExternalPaging.Resolved page)
    {
        ExternalPaging.Apply(Query, page);
        Query.Where(c => !c.IsWalkIn);
        Query.Select(c => new ExternalClientDto(c.Id, c.FullName, c.Phone, c.Email, c.CreatedAtUtc, c.ChangedAtUtc));
    }
}

public sealed class ExternalPetsPageSpec : Specification<Pet, ExternalPetDto>
{
    public ExternalPetsPageSpec(ExternalPaging.Resolved page)
    {
        ExternalPaging.Apply(Query, page);
        Query.Select(p => new ExternalPetDto(
            p.Id, p.ClientId, p.Name, p.Species != null ? p.Species.Name : null, p.BreedRef != null ? p.BreedRef.Name : p.Breed,
            p.Gender, p.BirthDate, p.IsNeutered, p.Status, p.ChangedAtUtc));
    }
}

public sealed class ExternalAppointmentsPageSpec : Specification<Appointment, ExternalAppointmentDto>
{
    public ExternalAppointmentsPageSpec(ExternalPaging.Resolved page)
    {
        ExternalPaging.Apply(Query, page);
        Query.Select(a => new ExternalAppointmentDto(
            a.Id, a.ClinicId, a.PetId, a.ScheduledAtUtc, a.DurationMinutes, a.AppointmentType, a.Status, a.ChangedAtUtc));
    }
}

public sealed class ExternalVaccinationsPageSpec : Specification<Vaccination, ExternalVaccinationDto>
{
    public ExternalVaccinationsPageSpec(ExternalPaging.Resolved page)
    {
        ExternalPaging.Apply(Query, page);
        Query.Select(v => new ExternalVaccinationDto(
            v.Id, v.ClinicId, v.PetId, v.VaccineName, v.Status, v.AppliedAtUtc, v.DueAtUtc, v.ChangedAtUtc));
    }
}

public sealed class GetExternalClientsQueryHandler : IRequestHandler<GetExternalClientsQuery, Result<ExternalPage<ExternalClientDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Client> _clients;

    public GetExternalClientsQueryHandler(ITenantContext tenantContext, IReadRepository<Client> clients)
    {
        _tenantContext = tenantContext;
        _clients = clients;
    }

    public ValueTask<Result<ExternalPage<ExternalClientDto>>> Handle(GetExternalClientsQuery request, CancellationToken ct)
        => ExternalListRunner.ListAsync(_tenantContext, _clients, request.Page, p => new ExternalClientsPageSpec(p), ct);
}

public sealed class GetExternalPetsQueryHandler : IRequestHandler<GetExternalPetsQuery, Result<ExternalPage<ExternalPetDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Pet> _pets;

    public GetExternalPetsQueryHandler(ITenantContext tenantContext, IReadRepository<Pet> pets)
    {
        _tenantContext = tenantContext;
        _pets = pets;
    }

    public ValueTask<Result<ExternalPage<ExternalPetDto>>> Handle(GetExternalPetsQuery request, CancellationToken ct)
        => ExternalListRunner.ListAsync(_tenantContext, _pets, request.Page, p => new ExternalPetsPageSpec(p), ct);
}

public sealed class GetExternalAppointmentsQueryHandler : IRequestHandler<GetExternalAppointmentsQuery, Result<ExternalPage<ExternalAppointmentDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Appointment> _appointments;

    public GetExternalAppointmentsQueryHandler(ITenantContext tenantContext, IReadRepository<Appointment> appointments)
    {
        _tenantContext = tenantContext;
        _appointments = appointments;
    }

    public ValueTask<Result<ExternalPage<ExternalAppointmentDto>>> Handle(GetExternalAppointmentsQuery request, CancellationToken ct)
        => ExternalListRunner.ListAsync(_tenantContext, _appointments, request.Page, p => new ExternalAppointmentsPageSpec(p), ct);
}

public sealed class GetExternalVaccinationsQueryHandler : IRequestHandler<GetExternalVaccinationsQuery, Result<ExternalPage<ExternalVaccinationDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Vaccination> _vaccinations;

    public GetExternalVaccinationsQueryHandler(ITenantContext tenantContext, IReadRepository<Vaccination> vaccinations)
    {
        _tenantContext = tenantContext;
        _vaccinations = vaccinations;
    }

    public ValueTask<Result<ExternalPage<ExternalVaccinationDto>>> Handle(GetExternalVaccinationsQuery request, CancellationToken ct)
        => ExternalListRunner.ListAsync(_tenantContext, _vaccinations, request.Page, p => new ExternalVaccinationsPageSpec(p), ct);
}
