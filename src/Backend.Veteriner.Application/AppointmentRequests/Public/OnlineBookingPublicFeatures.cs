using Backend.Veteriner.Application.Appointments.Availability;
using Backend.Veteriner.Application.Clinics.OnlineBooking;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.AppointmentRequests.Public;

/// <summary>Kliniğe özel bağlantı sayfası (kimliksiz; ADR-042 K1). Kod geçersiz/kapalıysa 404 <c>OnlineBooking.NotFound</c>.</summary>
public sealed record GetOnlineBookingPageQuery(string BookingCode) : IRequest<Result<OnlineBookingPageDto>>;

/// <summary>Bağlantıdan müsait saatler (online sınırlar uygulanır; hekim bilgisi dönmez).</summary>
public sealed record GetOnlineBookingAvailabilityQuery(string BookingCode, Guid? ReasonId, DateOnly? From, int Days)
    : IRequest<Result<IReadOnlyList<OnlineAvailabilityDayDto>>>;

/// <summary>
/// E-posta doğrulama kodu ister. Bekleme süresindeyse yeni kod gitmez; yanıt yine 204 (numaralandırma yok). Kapalı/geçersiz
/// bağlantıda 404.
/// </summary>
public sealed record RequestOnlineBookingCodeCommand(string BookingCode, string Email)
    : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => "OnlineBooking.CodeRequest";
    public string? AuditTarget => null;
}

/// <summary>
/// Bağlantıdan talep: e-posta koduyla doğrulanmadan talep oluşmaz (ADR-042 K1). Müşteri/hasta/hesap oluşmaz; talep iletişim
/// anlığı taşır. Saat müsait listede olmalı; aynı e-posta + klinik için en fazla 3 bekleyen talep. Neden boşsa "Diğer / emin
/// değilim": not zorunlu, kural domain'de (<see cref="AppointmentRequest.NoteRequiredCode"/>). <see cref="Channel"/> formun
/// taşıdığı kaynak değeri (TIER-016); izinli değilse klinik bağlantısı sayılır, talep reddedilmez.
/// </summary>
public sealed record CreateOnlineBookingRequestCommand(
    string BookingCode,
    string Email,
    string VerificationCode,
    string ContactName,
    string Phone,
    string PetName,
    string? PetSpecies,
    string? PetBreed,
    Guid? ReasonId,
    DateTime RequestedStartUtc,
    string? Note,
    string? Channel)
    : IRequest<Result<AppointmentRequestCreatedDto>>, IAuditableRequest
{
    public string AuditAction => "AppointmentRequest.Create.Link";
    public string? AuditTarget => null;
}

public sealed class GetOnlineBookingAvailabilityQueryValidator : AbstractValidator<GetOnlineBookingAvailabilityQuery>
{
    public GetOnlineBookingAvailabilityQueryValidator()
    {
        RuleFor(x => x.ReasonId).Must(id => id is null || id != Guid.Empty);
        RuleFor(x => x.Days).InclusiveBetween(1, AppointmentAvailabilityCalculator.MaxDays);
    }
}

public sealed class RequestOnlineBookingCodeCommandValidator : AbstractValidator<RequestOnlineBookingCodeCommand>
{
    public RequestOnlineBookingCodeCommandValidator()
        => RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(AppointmentRequest.EmailMaxLength);
}

public sealed class CreateOnlineBookingRequestCommandValidator : AbstractValidator<CreateOnlineBookingRequestCommand>
{
    public CreateOnlineBookingRequestCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(AppointmentRequest.EmailMaxLength);
        RuleFor(x => x.VerificationCode).NotEmpty();
        RuleFor(x => x.ContactName).NotEmpty().MaximumLength(AppointmentRequest.ContactNameMaxLength);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(AppointmentRequest.PhoneMaxLength);
        RuleFor(x => x.PetName).NotEmpty().MaximumLength(AppointmentRequest.PetNameMaxLength);
        RuleFor(x => x.PetSpecies).MaximumLength(AppointmentRequest.PetSpeciesMaxLength);
        RuleFor(x => x.PetBreed).MaximumLength(AppointmentRequest.PetBreedMaxLength);
        RuleFor(x => x.ReasonId).Must(id => id is null || id != Guid.Empty);
        RuleFor(x => x.RequestedStartUtc).Must(d => d != default).WithMessage("Talep saati zorunludur.");
        RuleFor(x => x.Note).MaximumLength(AppointmentRequest.NoteMaxLength);
    }
}

public sealed class GetOnlineBookingPageQueryHandler : IRequestHandler<GetOnlineBookingPageQuery, Result<OnlineBookingPageDto>>
{
    private readonly OnlineBookingClinicResolver _resolver;
    private readonly AppointmentRequestIntake _intake;

    public GetOnlineBookingPageQueryHandler(OnlineBookingClinicResolver resolver, AppointmentRequestIntake intake)
    {
        _resolver = resolver;
        _intake = intake;
    }

    public async ValueTask<Result<OnlineBookingPageDto>> Handle(GetOnlineBookingPageQuery request, CancellationToken ct)
    {
        var scope = await _resolver.ResolveByCodeAsync(request.BookingCode, ct);
        return scope.IsSuccess
            ? Result<OnlineBookingPageDto>.Success(await _intake.GetPageAsync(scope.Value!, ct))
            : Result<OnlineBookingPageDto>.Failure(scope.Error);
    }
}

public sealed class GetOnlineBookingAvailabilityQueryHandler
    : IRequestHandler<GetOnlineBookingAvailabilityQuery, Result<IReadOnlyList<OnlineAvailabilityDayDto>>>
{
    private readonly OnlineBookingClinicResolver _resolver;
    private readonly AppointmentRequestIntake _intake;

    public GetOnlineBookingAvailabilityQueryHandler(OnlineBookingClinicResolver resolver, AppointmentRequestIntake intake)
    {
        _resolver = resolver;
        _intake = intake;
    }

    public async ValueTask<Result<IReadOnlyList<OnlineAvailabilityDayDto>>> Handle(GetOnlineBookingAvailabilityQuery request, CancellationToken ct)
    {
        var scope = await _resolver.ResolveByCodeAsync(request.BookingCode, ct);
        return scope.IsSuccess
            ? await _intake.GetAvailabilityAsync(scope.Value!, request.ReasonId, request.From, request.Days, ct)
            : Result<IReadOnlyList<OnlineAvailabilityDayDto>>.Failure(scope.Error);
    }
}

public sealed class RequestOnlineBookingCodeCommandHandler : IRequestHandler<RequestOnlineBookingCodeCommand, Result>
{
    private readonly OnlineBookingClinicResolver _resolver;
    private readonly AppointmentRequestVerificationService _verification;
    private readonly TimeProvider _timeProvider;

    public RequestOnlineBookingCodeCommandHandler(
        OnlineBookingClinicResolver resolver,
        AppointmentRequestVerificationService verification,
        TimeProvider timeProvider)
    {
        _resolver = resolver;
        _verification = verification;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(RequestOnlineBookingCodeCommand request, CancellationToken ct)
    {
        var scope = await _resolver.ResolveByCodeAsync(request.BookingCode, ct);
        if (!scope.IsSuccess)
            return Result.Failure(scope.Error);

        var email = AppointmentRequest.NormalizeEmail(request.Email);
        if (email is null)
            return Result.Failure("AppointmentRequests.Validation", "E-posta geçersiz.");

        var s = scope.Value!;
        await _verification.IssueAndSendAsync(s.TenantId, s.ClinicId, s.Clinic.Name, email, _timeProvider.GetUtcNow().UtcDateTime, ct);
        return Result.Success();
    }
}

public sealed class CreateOnlineBookingRequestCommandHandler
    : IRequestHandler<CreateOnlineBookingRequestCommand, Result<AppointmentRequestCreatedDto>>
{
    private readonly OnlineBookingClinicResolver _resolver;
    private readonly AppointmentRequestIntake _intake;
    private readonly AppointmentRequestVerificationService _verification;
    private readonly TimeProvider _timeProvider;

    public CreateOnlineBookingRequestCommandHandler(
        OnlineBookingClinicResolver resolver,
        AppointmentRequestIntake intake,
        AppointmentRequestVerificationService verification,
        TimeProvider timeProvider)
    {
        _resolver = resolver;
        _intake = intake;
        _verification = verification;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AppointmentRequestCreatedDto>> Handle(CreateOnlineBookingRequestCommand request, CancellationToken ct)
    {
        var scopeResult = await _resolver.ResolveByCodeAsync(request.BookingCode, ct);
        if (!scopeResult.IsSuccess)
            return Result<AppointmentRequestCreatedDto>.Failure(scopeResult.Error);
        var scope = scopeResult.Value!;

        var email = AppointmentRequest.NormalizeEmail(request.Email);
        if (email is null)
            return Result<AppointmentRequestCreatedDto>.Failure("AppointmentRequests.Validation", "E-posta geçersiz.");

        var reason = await _intake.ResolveOnlineReasonAsync(scope, request.ReasonId, ct);
        if (!reason.IsSuccess)
            return Result<AppointmentRequestCreatedDto>.Failure(reason.Error);

        var startUtc = AppointmentRequestIntake.NormalizeUtc(request.RequestedStartUtc);
        var bookable = await _intake.EnsureBookableAsync(scope, reason.Value, startUtc, email, ct);
        if (!bookable.IsSuccess)
            return Result<AppointmentRequestCreatedDto>.Failure(bookable.Error);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var created = AppointmentRequest.CreateFromLink(
            scope.TenantId,
            scope.ClinicId,
            request.Channel,
            new PetText(request.PetName, request.PetSpecies, request.PetBreed),
            reason.Value,
            startUtc,
            request.ContactName,
            email,
            request.Phone,
            request.Note,
            utcNow);
        if (!created.IsSuccess)
            return Result<AppointmentRequestCreatedDto>.Failure(created.Error);

        // Kod son adımda tüketilir: saat/sınır/form hatasında kişi aynı kodla düzeltip tekrar deneyebilir.
        if (!await _verification.TryConsumeAsync(scope.TenantId, scope.ClinicId, email, request.VerificationCode, utcNow, ct))
        {
            await _verification.SaveAsync(ct);
            return Result<AppointmentRequestCreatedDto>.Failure(
                "OnlineBooking.InvalidCode",
                "Doğrulama kodu hatalı ya da süresi dolmuş.");
        }

        return Result<AppointmentRequestCreatedDto>.Success(await _intake.SubmitAsync(created.Value!, ct));
    }
}
