using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Appointments.Reasons;

/// <summary>Yeni randevu nedeni (ADR-042 K2). Aktif nedenler arasında ad benzersizdir; hekimler kliniğin aktif hekimi olmalı.</summary>
public sealed record CreateAppointmentReasonCommand(
    Guid ClinicId,
    string Name,
    int DefaultDurationMinutes,
    bool IsOnlineBookable,
    int SortOrder,
    IReadOnlyList<Guid>? VeterinarianUserIds)
    : IRequest<Result<AppointmentReasonDto>>, IAuditableRequest
{
    public string AuditAction => "AppointmentReason.Create";
    public string? AuditTarget => $"ClinicId={ClinicId}";
}

/// <summary>Neden alanlarını değiştirir; aktif/pasif ayrı komutla.</summary>
public sealed record UpdateAppointmentReasonCommand(
    Guid ClinicId,
    Guid ReasonId,
    string Name,
    int DefaultDurationMinutes,
    bool IsOnlineBookable,
    int SortOrder,
    IReadOnlyList<Guid>? VeterinarianUserIds)
    : IRequest<Result<AppointmentReasonDto>>, IAuditableRequest
{
    public string AuditAction => "AppointmentReason.Update";
    public string? AuditTarget => $"ReasonId={ReasonId}";
}

/// <summary>Nedeni pasife alır / etkinleştirir (idempotent). Etkinleştirmede ad benzersizliği tekrar denetlenir.</summary>
public sealed record SetAppointmentReasonActiveCommand(Guid ClinicId, Guid ReasonId, bool IsActive)
    : IRequest<Result<AppointmentReasonDto>>, IAuditableRequest
{
    public string AuditAction => IsActive ? "AppointmentReason.Activate" : "AppointmentReason.Deactivate";
    public string? AuditTarget => $"ReasonId={ReasonId}";
}

public sealed class CreateAppointmentReasonCommandValidator : AbstractValidator<CreateAppointmentReasonCommand>
{
    public CreateAppointmentReasonCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(AppointmentReason.NameMaxLength);
        RuleFor(x => x.DefaultDurationMinutes).Must(Appointment.IsValidDurationMinutes)
            .WithMessage($"Varsayılan süre {Appointment.MinDurationMinutes}-{Appointment.MaxDurationMinutes} dakika arasında olmalıdır.");
        RuleFor(x => x.VeterinarianUserIds).Must(v => v is null || v.Count <= AppointmentReason.MaxVeterinarians)
            .WithMessage($"En fazla {AppointmentReason.MaxVeterinarians} hekim seçilebilir.");
    }
}

public sealed class UpdateAppointmentReasonCommandValidator : AbstractValidator<UpdateAppointmentReasonCommand>
{
    public UpdateAppointmentReasonCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.ReasonId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(AppointmentReason.NameMaxLength);
        RuleFor(x => x.DefaultDurationMinutes).Must(Appointment.IsValidDurationMinutes)
            .WithMessage($"Varsayılan süre {Appointment.MinDurationMinutes}-{Appointment.MaxDurationMinutes} dakika arasında olmalıdır.");
        RuleFor(x => x.VeterinarianUserIds).Must(v => v is null || v.Count <= AppointmentReason.MaxVeterinarians)
            .WithMessage($"En fazla {AppointmentReason.MaxVeterinarians} hekim seçilebilir.");
    }
}

public sealed class SetAppointmentReasonActiveCommandValidator : AbstractValidator<SetAppointmentReasonActiveCommand>
{
    public SetAppointmentReasonActiveCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.ReasonId).NotEmpty();
    }
}

public sealed class CreateAppointmentReasonCommandHandler : IRequestHandler<CreateAppointmentReasonCommand, Result<AppointmentReasonDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IReadRepository<AppointmentReason> _reasonsRead;
    private readonly IRepository<AppointmentReason> _reasonsWrite;
    private readonly IClinicVeterinarianReader _veterinarians;
    private readonly TimeProvider _timeProvider;

    public CreateAppointmentReasonCommandHandler(
        ClinicDefinitionAccess access,
        IReadRepository<AppointmentReason> reasonsRead,
        IRepository<AppointmentReason> reasonsWrite,
        IClinicVeterinarianReader veterinarians,
        TimeProvider timeProvider)
    {
        _access = access;
        _reasonsRead = reasonsRead;
        _reasonsWrite = reasonsWrite;
        _veterinarians = veterinarians;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AppointmentReasonDto>> Handle(CreateAppointmentReasonCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<AppointmentReasonDto>.Failure(access.Error);
        var tenantId = access.Value;

        var vetIds = request.VeterinarianUserIds ?? [];
        var check = await AppointmentReasonWriteRules.EnsureNameAndVeterinariansAsync(_reasonsRead, _veterinarians, tenantId, request.ClinicId, request.Name, vetIds, null, ct);
        if (!check.IsSuccess)
            return Result<AppointmentReasonDto>.Failure(check.Error);

        AppointmentReason reason;
        try
        {
            reason = AppointmentReason.Create(
                tenantId,
                request.ClinicId,
                request.Name,
                request.DefaultDurationMinutes,
                request.IsOnlineBookable,
                request.SortOrder,
                vetIds,
                _timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (ArgumentException ex)
        {
            return Result<AppointmentReasonDto>.Failure("AppointmentReasons.Validation", ex.Message);
        }

        await _reasonsWrite.AddAsync(reason, ct);
        await _reasonsWrite.SaveChangesAsync(ct);
        return Result<AppointmentReasonDto>.Success(AppointmentReasonMapping.ToDto(reason));
    }
}

public sealed class UpdateAppointmentReasonCommandHandler : IRequestHandler<UpdateAppointmentReasonCommand, Result<AppointmentReasonDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IReadRepository<AppointmentReason> _reasonsRead;
    private readonly IRepository<AppointmentReason> _reasonsWrite;
    private readonly IClinicVeterinarianReader _veterinarians;
    private readonly TimeProvider _timeProvider;

    public UpdateAppointmentReasonCommandHandler(
        ClinicDefinitionAccess access,
        IReadRepository<AppointmentReason> reasonsRead,
        IRepository<AppointmentReason> reasonsWrite,
        IClinicVeterinarianReader veterinarians,
        TimeProvider timeProvider)
    {
        _access = access;
        _reasonsRead = reasonsRead;
        _reasonsWrite = reasonsWrite;
        _veterinarians = veterinarians;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AppointmentReasonDto>> Handle(UpdateAppointmentReasonCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<AppointmentReasonDto>.Failure(access.Error);
        var tenantId = access.Value;

        var reason = await _reasonsWrite.FirstOrDefaultAsync(
            new AppointmentReasonByIdSpec(tenantId, request.ClinicId, request.ReasonId), ct);
        if (reason is null)
            return AppointmentReasonWriteRules.NotFound();

        var vetIds = request.VeterinarianUserIds ?? [];
        // Pasif neden filtreli indeksin dışındadır; ad çakışması yalnız aktif neden için denetlenir.
        var check = await AppointmentReasonWriteRules.EnsureNameAndVeterinariansAsync(_reasonsRead, _veterinarians, 
            tenantId, request.ClinicId, reason.IsActive ? request.Name : null, vetIds, reason, ct);
        if (!check.IsSuccess)
            return Result<AppointmentReasonDto>.Failure(check.Error);

        try
        {
            reason.Update(
                request.Name,
                request.DefaultDurationMinutes,
                request.IsOnlineBookable,
                request.SortOrder,
                vetIds,
                _timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (ArgumentException ex)
        {
            return Result<AppointmentReasonDto>.Failure("AppointmentReasons.Validation", ex.Message);
        }

        await _reasonsWrite.UpdateAsync(reason, ct);
        await _reasonsWrite.SaveChangesAsync(ct);
        return Result<AppointmentReasonDto>.Success(AppointmentReasonMapping.ToDto(reason));
    }
}

public sealed class SetAppointmentReasonActiveCommandHandler : IRequestHandler<SetAppointmentReasonActiveCommand, Result<AppointmentReasonDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IReadRepository<AppointmentReason> _reasonsRead;
    private readonly IRepository<AppointmentReason> _reasonsWrite;
    private readonly TimeProvider _timeProvider;

    public SetAppointmentReasonActiveCommandHandler(
        ClinicDefinitionAccess access,
        IReadRepository<AppointmentReason> reasonsRead,
        IRepository<AppointmentReason> reasonsWrite,
        TimeProvider timeProvider)
    {
        _access = access;
        _reasonsRead = reasonsRead;
        _reasonsWrite = reasonsWrite;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AppointmentReasonDto>> Handle(SetAppointmentReasonActiveCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<AppointmentReasonDto>.Failure(access.Error);
        var tenantId = access.Value;

        var reason = await _reasonsWrite.FirstOrDefaultAsync(
            new AppointmentReasonByIdSpec(tenantId, request.ClinicId, request.ReasonId), ct);
        if (reason is null)
            return AppointmentReasonWriteRules.NotFound();

        if (request.IsActive && !reason.IsActive)
        {
            var duplicate = await _reasonsRead.AnyAsync(
                new ActiveAppointmentReasonByClinicAndNameSpec(
                    tenantId, request.ClinicId, AppointmentReasonMapping.NormalizeNameForLookup(reason.Name), reason.Id),
                ct);
            if (duplicate)
            {
                return Result<AppointmentReasonDto>.Failure(
                    "AppointmentReasons.DuplicateName",
                    "Aynı adda aktif bir neden var; etkinleştirmeden önce adı değiştirin.");
            }
        }

        if (reason.SetActive(request.IsActive, _timeProvider.GetUtcNow().UtcDateTime))
        {
            await _reasonsWrite.UpdateAsync(reason, ct);
            await _reasonsWrite.SaveChangesAsync(ct);
        }

        return Result<AppointmentReasonDto>.Success(AppointmentReasonMapping.ToDto(reason));
    }
}

/// <summary>Neden yazma kuralları (oluştur/güncelle ortak): aktif ad benzersizliği ve uygun hekimlerin kliniğin hekimi olması.</summary>
internal static class AppointmentReasonWriteRules
{
    /// <param name="name">Null ise ad benzersizliği denetlenmez (pasif neden).</param>
    /// <param name="existing">Güncellemede mevcut neden: zaten bağlı hekimler (sonradan hekim yetkisi kalkmış olsa da) korunabilir.</param>
    public static async Task<Result> EnsureNameAndVeterinariansAsync(
        IReadRepository<AppointmentReason> reasonsRead,
        IClinicVeterinarianReader veterinarians,
        Guid tenantId,
        Guid clinicId,
        string? name,
        IReadOnlyCollection<Guid> vetIds,
        AppointmentReason? existing,
        CancellationToken ct)
    {
        if (name is not null)
        {
            var duplicate = await reasonsRead.AnyAsync(
                new ActiveAppointmentReasonByClinicAndNameSpec(
                    tenantId, clinicId, AppointmentReasonMapping.NormalizeNameForLookup(name), existing?.Id),
                ct);
            if (duplicate)
                return Result.Failure("AppointmentReasons.DuplicateName", "Bu klinikte aynı adda aktif bir neden zaten var.");
        }

        var newIds = vetIds.Distinct()
            .Where(id => existing is null || existing.Veterinarians.All(v => v.UserId != id))
            .ToList();
        if (newIds.Count == 0)
            return Result.Success();

        var clinicVets = (await veterinarians.ListAsync(tenantId, clinicId, ct)).Select(v => v.UserId).ToHashSet();
        return newIds.All(clinicVets.Contains)
            ? Result.Success()
            : Result.Failure("AppointmentReasons.Validation", "Uygun hekimler bu kliniğe atanmış aktif hekimler (Veteriner) olmalıdır.");
    }

    public static Result<AppointmentReasonDto> NotFound()
        => Result<AppointmentReasonDto>.Failure("AppointmentReasons.NotFound", "Randevu nedeni bulunamadı.");
}
