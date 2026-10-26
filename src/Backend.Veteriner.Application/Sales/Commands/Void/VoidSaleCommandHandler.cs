using Backend.Veteriner.Application.Charges.Commands.Void;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Finance;
using Backend.Veteriner.Application.Payments.Commands.Void;
using Backend.Veteriner.Application.Sales.Contracts.Dtos;
using Backend.Veteriner.Application.Sales.Specs;
using Backend.Veteriner.Application.StockMovements.Commands.Reverse;
using Backend.Veteriner.Application.StockMovements.Specs;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Sales;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Sales.Commands.Void;

/// <summary>
/// Satışı tam iptal eder. Sıra: satış iptali kalıcılaşır (eşzamanlı ikinci iptal burada elenir), satışın kendi ödemesi
/// (<see cref="VoidPaymentCommand"/>), ücreti (<see cref="VoidChargeCommand"/>) ve stoğa iade seçilmişse her satırın çıkışı
/// (<see cref="ReverseStockMovementCommand"/>) mevcut komutlarla iptal edilir; kurallar tek yerde yaşar.
/// Atomiklik <see cref="ITransactionalRollbackOnFailureRequest"/> ile sağlanır: bir adım başarısız olursa hiçbiri uygulanmaz.
/// </summary>
public sealed class VoidSaleCommandHandler : IRequestHandler<VoidSaleCommand, Result<SaleDto>>
{
    private const string PaymentAlreadyVoidedCode = "Payments.AlreadyVoided";
    private const string ChargeAlreadyVoidedCode = "Charges.AlreadyVoided";
    private const string AlreadyReversedCode = "StockMovements.AlreadyReversed";

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<StockMovement> _stockMovements;
    private readonly IRepository<Sale> _sales;
    private readonly ISender _sender;
    private readonly TimeProvider _timeProvider;
    private readonly SaleDtoAssembler _assembler;

    public VoidSaleCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<StockMovement> stockMovements,
        IRepository<Sale> sales,
        ISender sender,
        TimeProvider timeProvider,
        SaleDtoAssembler assembler)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _stockMovements = stockMovements;
        _sales = sales;
        _sender = sender;
        _timeProvider = timeProvider;
        _assembler = assembler;
    }

    public async ValueTask<Result<SaleDto>> Handle(VoidSaleCommand request, CancellationToken ct)
    {
        var actor = await FinanceCorrectionActor.ResolveAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!actor.IsSuccess)
            return Result<SaleDto>.Failure(actor.Error);

        var (tenantId, userId) = actor.Value;

        var sale = await _sales.FirstOrDefaultAsync(new SaleForUpdateSpec(tenantId, request.Id), ct);
        if (sale is null
            || (_clinicContext.ClinicId is { } contextClinicId && sale.ClinicId != contextClinicId))
        {
            return Fail("Sales.NotFound", "Satış bulunamadı.");
        }

        var clinicAccess = await _clinicScopeResolver.ResolveAsync(tenantId, sale.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<SaleDto>.Failure(clinicAccess.Error);

        var stockOutcome = SaleVoidStockOutcome.StockReturned;
        if (!string.IsNullOrWhiteSpace(request.StockOutcome)
            && (!Enum.TryParse(request.StockOutcome, ignoreCase: true, out stockOutcome) || !Enum.IsDefined(stockOutcome)))
        {
            return Fail("Sales.Validation", "stockOutcome StockReturned veya WrittenOff olmalıdır.");
        }

        var voided = sale.Void(userId, request.Reason, stockOutcome, _timeProvider.GetUtcNow().UtcDateTime);
        if (!voided.IsSuccess)
            return Result<SaleDto>.Failure(voided.Error);

        try
        {
            await _sales.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Eşzamanlı iptal kazandı (RowVersion): hiçbir şey yazılmadı.
            return Fail("Sales.ConcurrencyConflict", "Satış eşzamanlı olarak güncellendi; yeniden okunup işlem tekrarlanmalı.");
        }

        var reason = sale.VoidReason!;

        // Ödeme "hiç alınmamış" sayılır (tahsisler geri alınır); ücret ancak ondan sonra iptal edilebilir.
        // Satış dışında (ödeme/ücret uçlarından) zaten iptal edilmiş bir adım iptali engellemez.
        if (sale.PaymentId is { } paymentId)
        {
            var payment = await _sender.Send(new VoidPaymentCommand(paymentId, reason), ct);
            if (!payment.IsSuccess && payment.Error.Code != PaymentAlreadyVoidedCode)
                return Result<SaleDto>.Failure(payment.Error);
        }

        // Satışın ücretine sonradan başka ödeme tahsis edilmişse Charges.VoidBlockedByAllocation döner; önce o ödemeler iptal edilir.
        if (sale.ChargeId is { } chargeId)
        {
            var charge = await _sender.Send(new VoidChargeCommand(chargeId, reason), ct);
            if (!charge.IsSuccess && charge.Error.Code != ChargeAlreadyVoidedCode)
                return Result<SaleDto>.Failure(charge.Error);
        }

        if (stockOutcome == SaleVoidStockOutcome.StockReturned)
        {
            var returned = await ReturnStockAsync(tenantId, sale, reason, ct);
            if (!returned.IsSuccess)
                return Result<SaleDto>.Failure(returned.Error);
        }

        return Result<SaleDto>.Success((await _assembler.AssembleAsync(tenantId, [sale], ct))[0]);
    }

    private async Task<Result> ReturnStockAsync(Guid tenantId, Sale sale, string reason, CancellationToken ct)
    {
        foreach (var line in sale.Lines)
        {
            var outMovement = await _stockMovements.FirstOrDefaultAsync(
                new StockMovementByReferenceSpec(
                    tenantId,
                    sale.ClinicId,
                    line.ProductId,
                    Sale.StockReferenceType,
                    line.Id,
                    StockMovementType.Out),
                ct);
            if (outMovement is null)
                return Result.Failure("Sales.StockMovementNotFound", "Satış satırının stok hareketi bulunamadı.");

            var reversal = await _sender.Send(
                new ReverseStockMovementCommand(outMovement.Id, reason, Notes: null, RequestId: null), ct);
            if (!reversal.IsSuccess)
            {
                return reversal.Error.Code == AlreadyReversedCode
                    ? Result.Failure("Sales.AlreadyVoided", "Satış zaten iptal edilmiş.")
                    : Result.Failure(reversal.Error);
            }
        }

        return Result.Success();
    }

    private static Result<SaleDto> Fail(string code, string message)
        => Result<SaleDto>.Failure(code, message);
}
