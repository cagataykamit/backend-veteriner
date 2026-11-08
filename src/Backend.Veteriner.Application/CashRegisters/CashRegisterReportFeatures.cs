using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Application.Reports.DailyTransactions.Queries;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Domain.CashCounts;
using Backend.Veteriner.Domain.CashRegisters;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.CashRegisters;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CashRegisterReportRowKind
{
    Collection = 1,
    CollectionVoid = 2,
    CreditRefund = 3,
    In = 4,
    Out = 5,
    TransferIn = 6,
    TransferOut = 7,
}

/// <summary>Kasa hareketleri raporunun satırı; <c>Balance</c> satırdan sonraki yürüyen bakiye.</summary>
public sealed record CashRegisterReportRowDto(
    DateOnly BusinessDate,
    DateTime AtUtc,
    CashRegisterReportRowKind Kind,
    Guid ReferenceId,
    Guid? CashRegisterId,
    string CashRegisterName,
    string Description,
    decimal InAmount,
    decimal OutAmount,
    decimal Balance,
    string? PerformedByName);

/// <summary>
/// Kasa hareketleri raporu. <c>OpeningBalance</c> dönemden önceki tüm hareketlerin neti (devreden), <c>Totals</c> dönemin dökümü,
/// <c>ClosingBalance</c> = devreden + dönem neti. Kasa verilmezse klinikteki tüm kasalar (aktarımlar birbirini götürür).
/// </summary>
public sealed record CashRegisterReportDto(
    Guid ClinicId,
    Guid? CashRegisterId,
    string? CashRegisterName,
    DateOnly From,
    DateOnly To,
    string Currency,
    decimal OpeningBalance,
    CashRegisterTotalsDto Totals,
    decimal ClosingBalance,
    IReadOnlyList<CashRegisterReportRowDto> Rows);

/// <summary>GET /api/v1/cash-registers/report. Tek klinik, en çok 366 gün.</summary>
public sealed record GetCashRegisterReportQuery(DateOnly From, DateOnly To, Guid? ClinicId, Guid? CashRegisterId)
    : IRequest<Result<CashRegisterReportDto>>;

public sealed record ExportCashRegisterReportQuery(DateOnly From, DateOnly To, Guid? ClinicId, Guid? CashRegisterId, RegisterExportFormat Format)
    : IRequest<Result<RegisterExportResult>>;

public sealed record CashRegisterReportExportRow(
    DateOnly? Date, DateTime? AtUtc, string? Register, string Movement, string? Description, decimal? In, decimal? Out, decimal Balance, string? PerformedBy);

/// <summary>Ekran ve dosya aynı yüklemeden geçer (ekran = CSV = XLSX).</summary>
internal sealed class CashRegisterReportLoader
{
    public const int MaxMovements = RegisterReportConstants.MaxExportRows;

    public static readonly RegisterExportSpec<CashRegisterReportExportRow> ExportSpec = new(
    [
        RegisterColumn<CashRegisterReportExportRow>.CalendarDate(null, "Tarih", r => r.Date),
        RegisterColumn<CashRegisterReportExportRow>.Time(null, "Saat", r => r.AtUtc),
        RegisterColumn<CashRegisterReportExportRow>.Text(null, "Kasa", r => r.Register),
        RegisterColumn<CashRegisterReportExportRow>.Text(null, "Hareket", r => r.Movement),
        RegisterColumn<CashRegisterReportExportRow>.Text(null, "Açıklama", r => r.Description),
        RegisterColumn<CashRegisterReportExportRow>.Money(null, "Giriş", r => r.In),
        RegisterColumn<CashRegisterReportExportRow>.Money(null, "Çıkış", r => r.Out),
        RegisterColumn<CashRegisterReportExportRow>.Money(null, "Bakiye", r => r.Balance),
        RegisterColumn<CashRegisterReportExportRow>.Text(null, "İşlemi Yapan", r => r.PerformedBy),
    ],
    "Kasa Hareketleri",
    "kasa-hareketleri",
    "Kasa Hareketleri");

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly ICashEventsReader _cashEvents;
    private readonly IReadRepository<CashRegister> _registers;
    private readonly IReadRepository<CashMovement> _movements;
    private readonly IReadRepository<CashMovementCategory> _categories;
    private readonly IReadRepository<Domain.Users.User> _users;

    public CashRegisterReportLoader(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        ICashEventsReader cashEvents,
        IReadRepository<CashRegister> registers,
        IReadRepository<CashMovement> movements,
        IReadRepository<CashMovementCategory> categories,
        IReadRepository<Domain.Users.User> users)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _scopeResolver = scopeResolver;
        _cashEvents = cashEvents;
        _registers = registers;
        _movements = movements;
        _categories = categories;
        _users = users;
    }

    public async Task<Result<CashRegisterReportDto>> LoadAsync(DateOnly from, DateOnly to, Guid? clinicId, Guid? cashRegisterId, CancellationToken ct)
    {
        var filter = await CashRegisterRules.ResolveClinicAsync(
            _tenantContext, _clinicContext, _scopeResolver, from, to, clinicId, "CashRegisters", ct);
        if (!filter.IsSuccess)
            return Result<CashRegisterReportDto>.Failure(filter.Error);
        var f = filter.Value!;
        var tenantId = f.TenantId;
        var clinic = f.ClinicId!.Value;

        ResolvedCashRegister? register = null;
        if (cashRegisterId is not null)
        {
            var resolved = await CashRegisterRules.ResolveAsync(_registers, tenantId, clinic, cashRegisterId, ct);
            if (!resolved.IsSuccess)
                return Result<CashRegisterReportDto>.Failure(resolved.Error);
            register = resolved.Value!;
        }

        var opening = await CashRegisterRules.LoadTotalsAsync(_cashEvents, _movements, tenantId, clinic, register, null, from.AddDays(-1), ct);

        var cashFilter = f with { CashRegister = register?.Scope, PaymentMethod = PaymentMethod.Cash };
        var cashRows = await _cashEvents.ReadAsync(cashFilter, CashSummaryLoader.MaxEvents, ct);
        var movements = await _movements.ListAsync(new CashMovementsSpec(tenantId, clinic, register?.Id, from, to, includeVoided: false), ct);
        if (cashRows.Count > CashSummaryLoader.MaxEvents || movements.Count > MaxMovements)
        {
            return Result<CashRegisterReportDto>.Failure(
                "CashRegisters.TooManyRows", "Kasa hareketleri raporu çok fazla satır içeriyor; tarih aralığını daraltın.");
        }

        var clinicRegisters = (await _registers.ListAsync(new CashRegistersByClinicSpec(tenantId, clinic, includeInactive: true), ct))
            .ToDictionary(r => r.Id);
        var mainName = clinicRegisters.Values.FirstOrDefault(r => r.IsMain)?.Name ?? CashRegister.MainName;
        string RegisterName(Guid? id) => id is { } v && clinicRegisters.TryGetValue(v, out var r) ? r.Name : mainName;
        var names = await CashMovementNames.LoadAsync(_registers, _categories, _users, tenantId, movements, ct);

        var rows = cashRows.Select(e => ToRow(e, RegisterName(e.CashRegisterId)))
            .Concat(movements.Select(m => ToRow(m, names)))
            .OrderBy(r => r.AtUtc).ThenBy(r => r.Kind).ThenBy(r => r.ReferenceId)
            .ToList();

        var balance = opening.NetAmount;
        var withBalance = new List<CashRegisterReportRowDto>(rows.Count);
        foreach (var row in rows)
        {
            balance += row.InAmount - row.OutAmount;
            withBalance.Add(row with { Balance = balance });
        }

        var cashNet = CashSummaryBuilder.Build(from, to, cashRows).ByMethod.Single(m => m.PaymentMethod == PaymentMethod.Cash).NetAmount;
        var totals = CashRegisterTotalsDto.From(cashNet, movements.Select(m => new CashMovementAmount(m.Kind, m.Amount)));

        return Result<CashRegisterReportDto>.Success(new CashRegisterReportDto(
            clinic,
            register?.Id,
            register?.Name,
            from,
            to,
            CashCount.DefaultCurrency,
            opening.NetAmount,
            totals,
            opening.NetAmount + totals.NetAmount,
            withBalance));
    }

    public static IReadOnlyList<CashRegisterReportExportRow> ToExportRows(CashRegisterReportDto report)
    {
        var rows = new List<CashRegisterReportExportRow>
        {
            new(report.From, null, report.CashRegisterName, "Devreden bakiye", null, null, null, report.OpeningBalance, null),
        };
        rows.AddRange(report.Rows.Select(r => new CashRegisterReportExportRow(
            r.BusinessDate, r.AtUtc, r.CashRegisterName, KindLabel(r.Kind), r.Description,
            r.InAmount == 0 ? null : r.InAmount, r.OutAmount == 0 ? null : r.OutAmount, r.Balance, r.PerformedByName)));
        rows.Add(new(report.To, null, report.CashRegisterName, "Dönem sonu bakiye", null, null, null, report.ClosingBalance, null));
        return rows;
    }

    public static string KindLabel(CashRegisterReportRowKind kind) => kind switch
    {
        CashRegisterReportRowKind.Collection => "Nakit tahsilat",
        CashRegisterReportRowKind.CollectionVoid => "Tahsilat iptali",
        CashRegisterReportRowKind.CreditRefund => "Kredi iadesi",
        CashRegisterReportRowKind.In => "Kasa girişi",
        CashRegisterReportRowKind.Out => "Kasa çıkışı",
        CashRegisterReportRowKind.TransferIn => "Gelen aktarım",
        CashRegisterReportRowKind.TransferOut => "Giden aktarım",
        _ => kind.ToString(),
    };

    private static CashRegisterReportRowDto ToRow(CashEventRow e, string registerName)
    {
        var (kind, isIn, by) = e switch
        {
            { IsCreditRefund: true } => (CashRegisterReportRowKind.CreditRefund, false, e.PerformedByName),
            { Voided: true } => (CashRegisterReportRowKind.CollectionVoid, false, e.PerformedByName),
            _ => (CashRegisterReportRowKind.Collection, true, e.CollectedByName),
        };
        var description = string.IsNullOrWhiteSpace(e.Reason) ? e.ClientName : $"{e.ClientName}: {e.Reason}";
        return new CashRegisterReportRowDto(
            OperationDayBounds.ToLocalDate(e.OccurredAtUtc), e.OccurredAtUtc, kind, e.PaymentId, e.CashRegisterId, registerName,
            description, isIn ? e.Amount : 0m, isIn ? 0m : e.Amount, 0m, by);
    }

    private static CashRegisterReportRowDto ToRow(CashMovement m, CashMovementNames names)
    {
        var kind = m.Kind switch
        {
            CashMovementKind.In => CashRegisterReportRowKind.In,
            CashMovementKind.Out => CashRegisterReportRowKind.Out,
            CashMovementKind.TransferIn => CashRegisterReportRowKind.TransferIn,
            _ => CashRegisterReportRowKind.TransferOut,
        };
        var head = m.IsTransfer
            ? (m.Kind == CashMovementKind.TransferIn ? "Kaynak: " : "Hedef: ") + names.Register(m.CounterpartCashRegisterId!.Value)
            : names.Category(m.CategoryId) ?? string.Empty;
        var description = string.IsNullOrWhiteSpace(m.Description) ? head : $"{head} — {m.Description}";

        // Geçmiş güne girilen hareket o günün başına yazılır; aynı gün girilen kayıt zamanıyla sıralanır.
        var at = OperationDayBounds.ToLocalDate(m.CreatedAtUtc) == m.BusinessDate
            ? m.CreatedAtUtc
            : OperationDayBounds.ForLocalDate(m.BusinessDate).DayStartUtc;
        return new CashRegisterReportRowDto(
            m.BusinessDate, at, kind, m.Id, m.CashRegisterId, names.Register(m.CashRegisterId), description,
            m.SignedAmount > 0 ? m.Amount : 0m, m.SignedAmount < 0 ? m.Amount : 0m, 0m, names.User(m.CreatedByUserId));
    }
}

public sealed class GetCashRegisterReportQueryHandler : IRequestHandler<GetCashRegisterReportQuery, Result<CashRegisterReportDto>>
{
    private readonly CashRegisterReportLoader _loader;

    public GetCashRegisterReportQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        ICashEventsReader cashEvents,
        IReadRepository<CashRegister> registers,
        IReadRepository<CashMovement> movements,
        IReadRepository<CashMovementCategory> categories,
        IReadRepository<Domain.Users.User> users)
        => _loader = new CashRegisterReportLoader(tenantContext, clinicContext, scopeResolver, cashEvents, registers, movements, categories, users);

    public ValueTask<Result<CashRegisterReportDto>> Handle(GetCashRegisterReportQuery request, CancellationToken ct)
        => new(_loader.LoadAsync(request.From, request.To, request.ClinicId, request.CashRegisterId, ct));
}

public sealed class ExportCashRegisterReportQueryHandler : IRequestHandler<ExportCashRegisterReportQuery, Result<RegisterExportResult>>
{
    private readonly CashRegisterReportLoader _loader;
    private readonly TimeProvider _timeProvider;

    public ExportCashRegisterReportQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        ICashEventsReader cashEvents,
        IReadRepository<CashRegister> registers,
        IReadRepository<CashMovement> movements,
        IReadRepository<CashMovementCategory> categories,
        IReadRepository<Domain.Users.User> users,
        TimeProvider timeProvider)
    {
        _loader = new CashRegisterReportLoader(tenantContext, clinicContext, scopeResolver, cashEvents, registers, movements, categories, users);
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<RegisterExportResult>> Handle(ExportCashRegisterReportQuery request, CancellationToken ct)
    {
        var report = await _loader.LoadAsync(request.From, request.To, request.ClinicId, request.CashRegisterId, ct);
        if (!report.IsSuccess)
            return Result<RegisterExportResult>.Failure(report.Error);

        return Result<RegisterExportResult>.Success(RegisterReportRunner.BuildFile(
            CashRegisterReportLoader.ExportSpec, CashRegisterReportLoader.ToExportRows(report.Value!), request.Format,
            request.From, request.To, _timeProvider.GetUtcNow().UtcDateTime));
    }
}
