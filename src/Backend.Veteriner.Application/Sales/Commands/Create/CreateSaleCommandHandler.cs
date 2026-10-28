using Backend.Veteriner.Application.Charges.Commands.Create;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Payments.Commands.Create;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Application.Sales.Commands.EnsureWalkInClient;
using Backend.Veteriner.Application.Sales.Contracts.Dtos;
using Backend.Veteriner.Application.Sales.Specs;
using Backend.Veteriner.Application.StockBatches;
using Backend.Veteriner.Application.StockMovements.Commands.Create;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Sales;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Sales.Commands.Create;

/// <summary>
/// Satışı yazar, ardından mevcut komutlarla aynı transaction'da satır başına stok düşümü (<see cref="CreateStockMovementCommand"/>),
/// satış başına tek ücret (<see cref="CreateChargeCommand"/>) ve varsa ödemeyi (<see cref="CreatePaymentCommand"/>) üretir
/// (stok/ücret/ödeme kuralları tek yerde yaşar). Atomiklik, komutun <see cref="ITransactionalRollbackOnFailureRequest"/> işaretiyle
/// <c>TransactionBehavior</c> tarafından sağlanır: herhangi bir adım başarısız olursa satış dahil hiçbir kayıt kalmaz.
/// </summary>
public sealed class CreateSaleCommandHandler : IRequestHandler<CreateSaleCommand, Result<SaleCreateResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Product> _products;
    private readonly IReadRepository<Sale> _salesRead;
    private readonly IRepository<Sale> _salesWrite;
    private readonly ISender _sender;
    private readonly TimeProvider _timeProvider;
    private readonly SaleDtoAssembler _assembler;

    public CreateSaleCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Client> clients,
        IReadRepository<Product> products,
        IReadRepository<Sale> salesRead,
        IRepository<Sale> salesWrite,
        ISender sender,
        TimeProvider timeProvider,
        SaleDtoAssembler assembler)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _clients = clients;
        _products = products;
        _salesRead = salesRead;
        _salesWrite = salesWrite;
        _sender = sender;
        _timeProvider = timeProvider;
        _assembler = assembler;
    }

    public async ValueTask<Result<SaleCreateResultDto>> Handle(CreateSaleCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Fail(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
            return Fail("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Fail("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Fail("Tenants.TenantInactive", "Pasif kiracı için satış oluşturulamaz.");

        if (ClinicContextRule.Conflicts(request.ClinicId, _clinicContext.ClinicId))
        {
            return Result<SaleCreateResultDto>.Failure(ClinicContextRule.Mismatch("Sales"));
        }

        if ((_clinicContext.ClinicId ?? request.ClinicId) is not { } clinicId)
            return Fail("Sales.Validation", "ClinicId zorunludur (aktif klinik bağlamı yok).");

        var clinicAccess = await _clinicScopeResolver.ResolveAsync(tenantId, clinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<SaleCreateResultDto>.Failure(clinicAccess.Error);

        if (request.RequestId == Guid.Empty)
            return Fail("Sales.RequestIdRequired", "requestId zorunludur (çift istek koruması).");

        if (request.Lines is not { Count: > 0 } requestLines)
            return Fail("Sales.Validation", "Satışta en az bir satır olmalıdır.");

        // Tekrar istek: aynı RequestId aynı içerikle ilk satışı döndürür, stok/ücret/ödeme değişmez.
        var fingerprint = SaleRequestFingerprint.Compute(request, clinicId);
        var duplicate = await _salesRead.FirstOrDefaultAsync(new SaleByRequestIdSpec(tenantId, request.RequestId), ct);
        if (duplicate is not null)
            return await ReplayAsync(duplicate, fingerprint, ct);

        var paymentMethod = ParsePaymentMethod(request.Payment);
        if (!paymentMethod.IsSuccess)
            return Result<SaleCreateResultDto>.Failure(paymentMethod.Error);

        var drafts = await BuildDraftsAsync(tenantId, requestLines, ct);
        if (!drafts.IsSuccess)
            return Result<SaleCreateResultDto>.Failure(drafts.Error);

        var clientResult = await ResolveClientAsync(tenantId, request.ClientId, ct);
        if (!clientResult.IsSuccess)
            return Result<SaleCreateResultDto>.Failure(clientResult.Error);

        var client = clientResult.Value!;
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var created = Sale.Create(
            tenantId,
            clinicId,
            client.Id,
            nowUtc,
            request.Notes,
            drafts.Value!,
            userId,
            nowUtc,
            request.RequestId,
            fingerprint);
        if (!created.IsSuccess)
            return Result<SaleCreateResultDto>.Failure(created.Error);

        var sale = created.Value!;

        var paymentAmount = ResolvePaymentAmount(client, request.Payment, sale.TotalAmount);
        if (!paymentAmount.IsSuccess)
            return Result<SaleCreateResultDto>.Failure(paymentAmount.Error);

        try
        {
            // Kayıt hemen yapılır (işlem içinde); requestId benzersizliği stoktan önce elenir.
            await _salesWrite.AddAsync(sale, ct);
            await _salesWrite.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Eşzamanlı çift istek: benzersiz indeks kaybedene çarptı; hiçbir şey yazılmadı, kazanan döner.
            var winner = await _salesRead.FirstOrDefaultAsync(new SaleByRequestIdSpec(tenantId, request.RequestId), ct);
            if (winner is null)
                throw;

            return await ReplayAsync(winner, fingerprint, ct);
        }

        var stockStep = await DropStockAsync(sale, requestLines, clinicId, ct);
        if (!stockStep.IsSuccess)
            return Result<SaleCreateResultDto>.Failure(stockStep.Error);

        // Satış başına tek ücret; RequestId satışın kimliğidir (türetilmiş, ayrı anahtar gerekmez).
        var charge = await _sender.Send(
            new CreateChargeCommand(
                clinicId,
                client.Id,
                PetId: null,
                VisitId: null,
                ExaminationId: null,
                Sale.ChargeDescription,
                Quantity: 1m,
                UnitPrice: sale.TotalAmount,
                RequestId: sale.Id),
            ct);
        if (!charge.IsSuccess)
            return Result<SaleCreateResultDto>.Failure(charge.Error);

        var chargeId = charge.Value!.Charge.Id;

        Guid? paymentId = null;
        if (request.Payment is { } payment)
        {
            var amount = paymentAmount.Value;
            var paid = await _sender.Send(
                new CreatePaymentCommand(
                    clinicId,
                    client.Id,
                    PetId: null,
                    AppointmentId: null,
                    ExaminationId: null,
                    amount,
                    Sale.Currency,
                    paymentMethod.Value,
                    payment.PaidAtUtc ?? nowUtc,
                    payment.Notes,
                    RequestId: sale.Id,
                    Allocations: [new PaymentAllocationLineDto(chargeId, amount)]),
                ct);
            if (!paid.IsSuccess)
                return Result<SaleCreateResultDto>.Failure(paid.Error);

            paymentId = paid.Value!.Id;
        }

        sale.AttachFinance(chargeId, paymentId);
        try
        {
            await _salesWrite.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail(
                "Sales.ConcurrencyConflict",
                "Satış eşzamanlı olarak güncellendi; işlem tekrarlanmalı.");
        }

        return Result<SaleCreateResultDto>.Success(await BuildResultAsync(sale, created: true, ct));
    }

    /// <summary>
    /// Ürünleri doğrular (var, aktif, aşı/kontrollü değil) ve satır girdilerini üretir; fiyat yoksa ürün fiyatı kullanılır.
    /// </summary>
    private async Task<Result<IReadOnlyList<SaleLineDraft>>> BuildDraftsAsync(
        Guid tenantId, IReadOnlyList<CreateSaleLineDto> requestLines, CancellationToken ct)
    {
        var productIds = requestLines.Select(l => l.ProductId).Distinct().ToArray();
        var products = (await _products.ListAsync(new ProductsByIdsSpec(tenantId, productIds), ct))
            .ToDictionary(p => p.Id);

        var drafts = new List<SaleLineDraft>(requestLines.Count);
        foreach (var line in requestLines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
                return DraftFail("Products.NotFound", "Ürün bulunamadı veya kiracıya ait değil.");

            if (!product.IsActive)
                return DraftFail("Sales.ProductInactive", $"Pasif ürün satılamaz: {product.Name}.");

            if (!product.IsStockTracked)
                return DraftFail("Products.NotStockTracked", $"Hizmet kalemi doğrudan satılamaz: {product.Name}.");

            // ADR-016 karar 3: aşı ve narkotik/psikotropik ilk sürümde yalnız hekim uygulamasıyla kullanılır.
            if (product.Kind == ProductKind.Vaccine)
                return DraftFail("Sales.VaccineNotSellable", $"Aşı ürünü doğrudan satılamaz: {product.Name}.");

            if (product.ControlledClass != ControlledSubstanceClass.None)
                return DraftFail(
                    "Sales.ControlledSubstanceNotSellable",
                    $"Narkotik/psikotropik ürün doğrudan satılamaz: {product.Name}.");

            drafts.Add(new SaleLineDraft(
                product.Id,
                product.Name,
                product.Unit,
                line.Quantity,
                line.UnitPrice ?? product.UnitPrice));
        }

        return Result<IReadOnlyList<SaleLineDraft>>.Success(drafts);
    }

    /// <summary>Müşteri verilmediyse (veya peşin müşterinin kimliği verildiyse) peşin müşteri; yoksa aynı işlemde oluşturulur.</summary>
    private async Task<Result<Client>> ResolveClientAsync(Guid tenantId, Guid? clientId, CancellationToken ct)
    {
        var id = clientId;
        if (id is null || id == Guid.Empty)
        {
            var walkIn = await _sender.Send(new EnsureWalkInClientCommand(), ct);
            if (!walkIn.IsSuccess)
                return Result<Client>.Failure(walkIn.Error);

            id = walkIn.Value!.ClientId;
        }

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, id.Value), ct);
        return client is null
            ? Result<Client>.Failure("Clients.NotFound", "Müşteri bulunamadı veya kiracıya ait değil.")
            : Result<Client>.Success(client);
    }

    private static Result<PaymentMethod> ParsePaymentMethod(CreateSalePaymentDto? payment)
    {
        if (payment is null)
            return Result<PaymentMethod>.Success(default);

        return Enum.TryParse<PaymentMethod>(payment.Method, ignoreCase: true, out var method) && Enum.IsDefined(method)
            ? Result<PaymentMethod>.Success(method)
            : Result<PaymentMethod>.Failure("Sales.Validation", "Ödeme yöntemi Cash, Card veya Transfer olmalıdır.");
    }

    /// <summary>
    /// Peşin müşteride ödeme zorunlu ve tam tutardır; müşterilide ödeme opsiyoneldir ve toplamı aşamaz
    /// (eksik kalan veresiye olarak müşteri bakiyesinde açık kalır).
    /// </summary>
    private static Result<decimal> ResolvePaymentAmount(Client client, CreateSalePaymentDto? payment, decimal total)
    {
        if (payment is null)
        {
            return client.IsWalkIn
                ? Result<decimal>.Failure(
                    "Sales.WalkInPaymentRequired",
                    "Peşin müşteri satışında ödeme zorunludur.")
                : Result<decimal>.Success(0m);
        }

        var amount = payment.Amount ?? total;
        if (client.IsWalkIn)
        {
            return amount == total
                ? Result<decimal>.Success(amount)
                : Result<decimal>.Failure(
                    "Sales.WalkInPaymentMustBeFull",
                    "Peşin müşteri satışı tam tutarla ödenmelidir.");
        }

        if (amount <= 0)
            return Result<decimal>.Failure("Sales.Validation", "Ödeme tutarı sıfırdan büyük olmalıdır.");

        return amount > total
            ? Result<decimal>.Failure("Sales.PaymentExceedsTotal", "Ödeme tutarı satış toplamını aşamaz.")
            : Result<decimal>.Success(amount);
    }

    /// <summary>
    /// Her satır için stok çıkışı (kaynak = satır kimliği). Süresi geçmiş partiden satış yapılamaz; seçilen parti satıra bağlanır.
    /// </summary>
    private async Task<Result> DropStockAsync(
        Sale sale, IReadOnlyList<CreateSaleLineDto> requestLines, Guid clinicId, CancellationToken ct)
    {
        for (var i = 0; i < sale.Lines.Count; i++)
        {
            var line = sale.Lines[i];
            var movement = await _sender.Send(
                new CreateStockMovementCommand(
                    clinicId,
                    line.ProductId,
                    StockMovementType.Out,
                    line.Quantity,
                    UnitCost: null,
                    Reason: "Satış",
                    ReferenceType: Sale.StockReferenceType,
                    ReferenceId: line.Id,
                    OccurredAtUtc: sale.SoldAtUtc,
                    Notes: null,
                    BatchId: requestLines[i].BatchId,
                    NotExpiredOn: OperationDayBounds.ToLocalDate(sale.SoldAtUtc)),
                ct);
            if (!movement.IsSuccess)
                return Result.Failure(MapStockError(movement.Error));

            if (movement.Value!.Movement.BatchId is { } batchId)
                line.LinkBatch(batchId);
        }

        return Result.Success();
    }

    private async Task<Result<SaleCreateResultDto>> ReplayAsync(Sale existing, string fingerprint, CancellationToken ct)
    {
        if (existing.IsVoided)
        {
            return Fail(
                "Sales.RequestIdVoided",
                "Bu requestId ile kaydedilen satış iptal edilmiş; yeni bir requestId kullanılmalı.");
        }

        if (!string.Equals(existing.RequestHash, fingerprint, StringComparison.Ordinal))
            return Fail("Sales.DuplicateRequestId", "Bu requestId başka bir satış isteği için kullanılmış.");

        return Result<SaleCreateResultDto>.Success(await BuildResultAsync(existing, created: false, ct));
    }

    private async Task<SaleCreateResultDto> BuildResultAsync(Sale sale, bool created, CancellationToken ct)
    {
        var dto = (await _assembler.AssembleAsync(sale.TenantId, [sale], ct))[0];
        var stocks = await _assembler.StocksAsync(sale, ct);
        return new SaleCreateResultDto(created, dto, stocks);
    }

    private static Error MapStockError(Error error)
        => error.Code switch
        {
            StockBatchResolver.InsufficientStockCode => new Error("Sales.InsufficientStock", error.Message),
            StockBatchResolver.ExpiredCode => new Error("Sales.BatchExpired", error.Message),
            _ => error,
        };

    private static Result<IReadOnlyList<SaleLineDraft>> DraftFail(string code, string message)
        => Result<IReadOnlyList<SaleLineDraft>>.Failure(code, message);

    private static Result<SaleCreateResultDto> Fail(string code, string message)
        => Result<SaleCreateResultDto>.Failure(code, message);
}
