using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Appointments.Resources;

/// <summary>Yeni oda ya da cihaz (ADR-053 K5). Aktif kaynaklar arasında ad klinik içinde benzersizdir.</summary>
public sealed record CreateAppointmentResourceCommand(
    Guid ClinicId,
    string Name,
    AppointmentResourceType Type,
    int Capacity,
    int SortOrder)
    : IRequest<Result<AppointmentResourceDto>>, IAuditableRequest
{
    public string AuditAction => "AppointmentResource.Create";
    public string? AuditTarget => $"ClinicId={ClinicId}";
}

/// <summary>Kaynak alanlarını değiştirir; aktif/pasif ayrı komutla. Kapasite düşürülürse mevcut randevular etkilenmez.</summary>
public sealed record UpdateAppointmentResourceCommand(
    Guid ClinicId,
    Guid ResourceId,
    string Name,
    AppointmentResourceType Type,
    int Capacity,
    int SortOrder)
    : IRequest<Result<AppointmentResourceDto>>, IAuditableRequest
{
    public string AuditAction => "AppointmentResource.Update";
    public string? AuditTarget => $"ResourceId={ResourceId}";
}

/// <summary>Kaynağı pasife alır / etkinleştirir (idempotent). Etkinleştirmede ad benzersizliği tekrar denetlenir.</summary>
public sealed record SetAppointmentResourceActiveCommand(Guid ClinicId, Guid ResourceId, bool IsActive)
    : IRequest<Result<AppointmentResourceDto>>, IAuditableRequest
{
    public string AuditAction => IsActive ? "AppointmentResource.Activate" : "AppointmentResource.Deactivate";
    public string? AuditTarget => $"ResourceId={ResourceId}";
}

public sealed class CreateAppointmentResourceCommandValidator : AbstractValidator<CreateAppointmentResourceCommand>
{
    public CreateAppointmentResourceCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        AppointmentResourceRules.Fields(this, x => x.Name, x => x.Type, x => x.Capacity);
    }
}

public sealed class UpdateAppointmentResourceCommandValidator : AbstractValidator<UpdateAppointmentResourceCommand>
{
    public UpdateAppointmentResourceCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.ResourceId).NotEmpty();
        AppointmentResourceRules.Fields(this, x => x.Name, x => x.Type, x => x.Capacity);
    }
}

public sealed class SetAppointmentResourceActiveCommandValidator : AbstractValidator<SetAppointmentResourceActiveCommand>
{
    public SetAppointmentResourceActiveCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.ResourceId).NotEmpty();
    }
}

public sealed class CreateAppointmentResourceCommandHandler
    : IRequestHandler<CreateAppointmentResourceCommand, Result<AppointmentResourceDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IReadRepository<AppointmentResource> _read;
    private readonly IRepository<AppointmentResource> _write;
    private readonly TimeProvider _timeProvider;

    public CreateAppointmentResourceCommandHandler(
        ClinicDefinitionAccess access,
        IReadRepository<AppointmentResource> read,
        IRepository<AppointmentResource> write,
        TimeProvider timeProvider)
    {
        _access = access;
        _read = read;
        _write = write;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AppointmentResourceDto>> Handle(CreateAppointmentResourceCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<AppointmentResourceDto>.Failure(access.Error);
        var tenantId = access.Value;

        if (await AppointmentResourceRules.NameTakenAsync(_read, tenantId, request.ClinicId, request.Name, null, ct))
            return AppointmentResourceRules.DuplicateName();

        AppointmentResource resource;
        try
        {
            resource = AppointmentResource.Create(
                tenantId, request.ClinicId, request.Name, request.Type, request.Capacity, request.SortOrder,
                _timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (ArgumentException ex)
        {
            return Result<AppointmentResourceDto>.Failure("AppointmentResources.Validation", ex.Message);
        }

        await _write.AddAsync(resource, ct);
        await _write.SaveChangesAsync(ct);
        return Result<AppointmentResourceDto>.Success(AppointmentResourceMapping.ToDto(resource));
    }
}

public sealed class UpdateAppointmentResourceCommandHandler
    : IRequestHandler<UpdateAppointmentResourceCommand, Result<AppointmentResourceDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IReadRepository<AppointmentResource> _read;
    private readonly IRepository<AppointmentResource> _write;
    private readonly TimeProvider _timeProvider;

    public UpdateAppointmentResourceCommandHandler(
        ClinicDefinitionAccess access,
        IReadRepository<AppointmentResource> read,
        IRepository<AppointmentResource> write,
        TimeProvider timeProvider)
    {
        _access = access;
        _read = read;
        _write = write;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AppointmentResourceDto>> Handle(UpdateAppointmentResourceCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<AppointmentResourceDto>.Failure(access.Error);
        var tenantId = access.Value;

        var resource = await _write.FirstOrDefaultAsync(new AppointmentResourceByIdSpec(tenantId, request.ClinicId, request.ResourceId), ct);
        if (resource is null)
            return AppointmentResourceRules.NotFound();

        // Pasif kaynak filtreli indeksin dışındadır; ad çakışması yalnız aktif kaynak için denetlenir.
        if (resource.IsActive && await AppointmentResourceRules.NameTakenAsync(_read, tenantId, request.ClinicId, request.Name, resource.Id, ct))
            return AppointmentResourceRules.DuplicateName();

        try
        {
            resource.Update(request.Name, request.Type, request.Capacity, request.SortOrder, _timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (ArgumentException ex)
        {
            return Result<AppointmentResourceDto>.Failure("AppointmentResources.Validation", ex.Message);
        }

        await _write.UpdateAsync(resource, ct);
        await _write.SaveChangesAsync(ct);
        return Result<AppointmentResourceDto>.Success(AppointmentResourceMapping.ToDto(resource));
    }
}

public sealed class SetAppointmentResourceActiveCommandHandler
    : IRequestHandler<SetAppointmentResourceActiveCommand, Result<AppointmentResourceDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IReadRepository<AppointmentResource> _read;
    private readonly IRepository<AppointmentResource> _write;
    private readonly TimeProvider _timeProvider;

    public SetAppointmentResourceActiveCommandHandler(
        ClinicDefinitionAccess access,
        IReadRepository<AppointmentResource> read,
        IRepository<AppointmentResource> write,
        TimeProvider timeProvider)
    {
        _access = access;
        _read = read;
        _write = write;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AppointmentResourceDto>> Handle(SetAppointmentResourceActiveCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<AppointmentResourceDto>.Failure(access.Error);
        var tenantId = access.Value;

        var resource = await _write.FirstOrDefaultAsync(new AppointmentResourceByIdSpec(tenantId, request.ClinicId, request.ResourceId), ct);
        if (resource is null)
            return AppointmentResourceRules.NotFound();

        if (request.IsActive && !resource.IsActive
            && await AppointmentResourceRules.NameTakenAsync(_read, tenantId, request.ClinicId, resource.Name, resource.Id, ct))
        {
            return Result<AppointmentResourceDto>.Failure(
                "AppointmentResources.DuplicateName",
                "Aynı adda aktif bir oda ya da cihaz var; etkinleştirmeden önce adı değiştirin.");
        }

        if (resource.SetActive(request.IsActive, _timeProvider.GetUtcNow().UtcDateTime))
        {
            await _write.UpdateAsync(resource, ct);
            await _write.SaveChangesAsync(ct);
        }

        return Result<AppointmentResourceDto>.Success(AppointmentResourceMapping.ToDto(resource));
    }
}

/// <summary>Kaynak yazma kuralları (oluştur/güncelle ortak): alan doğrulaması ve aktif ad benzersizliği.</summary>
internal static class AppointmentResourceRules
{
    public static void Fields<T>(
        AbstractValidator<T> v,
        System.Linq.Expressions.Expression<Func<T, string>> name,
        System.Linq.Expressions.Expression<Func<T, AppointmentResourceType>> type,
        System.Linq.Expressions.Expression<Func<T, int>> capacity)
    {
        v.RuleFor(name).NotEmpty().MaximumLength(AppointmentResource.NameMaxLength);
        v.RuleFor(type).IsInEnum().WithMessage("Kaynak türü Oda (Room) ya da Cihaz (Equipment) olmalıdır.");
        v.RuleFor(capacity).Must(AppointmentResource.IsValidCapacity)
            .WithMessage($"Kapasite {AppointmentResource.MinCapacity}-{AppointmentResource.MaxCapacity} arasında olmalıdır.");
    }

    public static Task<bool> NameTakenAsync(
        IReadRepository<AppointmentResource> read,
        Guid tenantId,
        Guid clinicId,
        string name,
        Guid? excludeId,
        CancellationToken ct)
        => read.AnyAsync(
            new ActiveAppointmentResourceByClinicAndNameSpec(
                tenantId, clinicId, AppointmentResourceMapping.NormalizeNameForLookup(name), excludeId),
            ct);

    public static Result<AppointmentResourceDto> DuplicateName()
        => Result<AppointmentResourceDto>.Failure("AppointmentResources.DuplicateName", "Bu klinikte aynı adda aktif bir oda ya da cihaz zaten var.");

    public static Result<AppointmentResourceDto> NotFound()
        => Result<AppointmentResourceDto>.Failure("AppointmentResources.NotFound", "Oda ya da cihaz bulunamadı.");
}
