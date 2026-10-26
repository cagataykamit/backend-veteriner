using System.Buffers.Binary;
using System.Security.Cryptography;
using Backend.Veteriner.Application.GoodsReceipts.Contracts.Dtos;
using Backend.Veteriner.Application.StockMovements.Commands.Create;
using Backend.Veteriner.Application.StockMovements.Contracts.Dtos;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using FluentValidation.Results;
using Mediator;

namespace Backend.Veteriner.Application.GoodsReceipts.Commands.Create;

/// <summary>
/// Satır başına mevcut <see cref="CreateStockMovementCommand"/> (<c>In</c>) çağrılır (lot/SKT zorunluluğu, geçmiş SKT onayı, ürün/klinik
/// kontrolleri, parti ve stok yazımı tek yerde yaşar). Atomiklik, komutun <see cref="Common.Abstractions.ITransactionalRollbackOnFailureRequest"/>
/// işaretiyle <c>TransactionBehavior</c> tarafından sağlanır. Idempotency: her satıra <c>requestId</c> + sıradan türetilen sabit bir requestId verilir.
/// </summary>
public sealed class CreateGoodsReceiptCommandHandler
    : IRequestHandler<CreateGoodsReceiptCommand, Result<GoodsReceiptCreateResult>>
{
    private const string MovementReason = "Mal kabul";

    private readonly ISender _sender;
    private readonly TimeProvider _timeProvider;

    public CreateGoodsReceiptCommandHandler(ISender sender, TimeProvider timeProvider)
    {
        _sender = sender;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<GoodsReceiptCreateResult>> Handle(CreateGoodsReceiptCommand request, CancellationToken ct)
    {
        var lines = request.Lines!;
        var occurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var written = new List<StockMovementCreateResult>(lines.Count);

        for (var i = 0; i < lines.Count; i++)
        {
            var result = await SendLineAsync(request, lines[i], i, occurredAtUtc, ct);
            if (!result.IsSuccess)
            {
                return Result<GoodsReceiptCreateResult>.Failure(
                    result.Error.Code,
                    $"Satır {i + 1}: {result.Error.Message}");
            }

            written.Add(result.Value!);
        }

        // Tamamı tekrar (aynı istek) ya da tamamı yeni olmalı; karışık sonuç requestId'nin başka içerikle kullanıldığını gösterir.
        var createdCount = written.Count(w => w.Created);
        var mixed = createdCount != 0 && createdCount != written.Count;
        var changed = createdCount == 0 && written.Where((w, i) => w.Movement.Quantity != lines[i].Quantity).Any();
        if (mixed || changed)
        {
            return Result<GoodsReceiptCreateResult>.Failure(
                "GoodsReceipts.DuplicateRequestId",
                "Bu requestId başka bir mal kabul isteği için kullanılmış.");
        }

        return Result<GoodsReceiptCreateResult>.Success(
            new GoodsReceiptCreateResult(createdCount != 0, request.ClinicId, written.Select(w => w.Movement).ToList()));
    }

    private async Task<Result<StockMovementCreateResult>> SendLineAsync(
        CreateGoodsReceiptCommand request,
        GoodsReceiptLineDto line,
        int index,
        DateTime occurredAtUtc,
        CancellationToken ct)
    {
        var batch = new StockBatchDetails(
            line.LotNumber,
            line.ExpiryDate,
            request.SupplierName,
            request.InvoiceNumber,
            request.InvoiceDate,
            request.ReceivedDate,
            request.DispatchedDate,
            request.TransportMethod,
            request.ColdChainMonitor);

        try
        {
            return await _sender.Send(
                new CreateStockMovementCommand(
                    request.ClinicId,
                    line.ProductId,
                    StockMovementType.In,
                    line.Quantity,
                    line.UnitCost,
                    MovementReason,
                    ReferenceType: null,
                    ReferenceId: null,
                    OccurredAtUtc: occurredAtUtc,
                    Notes: request.Notes,
                    RequestId: LineRequestId(request.RequestId, index),
                    Batch: batch,
                    AcknowledgeExpired: line.AcknowledgeExpired),
                ct);
        }
        catch (ValidationException ex)
        {
            // Satır doğrulaması mevcut giriş validator'ındadır; hata yolu istek gövdesindeki satıra çevrilir.
            throw new ValidationException(ex.Errors.Select(e => new ValidationFailure(
                MapPropertyPath(e.PropertyName, index), e.ErrorMessage)));
        }
    }

    /// <summary>Satır alanları <c>Lines[i].X</c>; ortak başlık alanları (firma, fatura, nakil) <c>Batch.X</c> yerine doğrudan <c>X</c> olur.</summary>
    private static string MapPropertyPath(string property, int index)
    {
        if (property == "Batch.LotNumber")
            return $"Lines[{index}].LotNumber";

        if (property.StartsWith("Batch.", StringComparison.Ordinal))
            return property["Batch.".Length..];

        return property is "Quantity" or "UnitCost" or "ProductId"
            ? $"Lines[{index}].{property}"
            : property;
    }

    /// <summary>Aynı mal kabul requestId'si ve satır sırası her zaman aynı hareket requestId'sini üretir.</summary>
    private static Guid LineRequestId(Guid receiptRequestId, int index)
    {
        Span<byte> input = stackalloc byte[20];
        receiptRequestId.TryWriteBytes(input);
        BinaryPrimitives.WriteInt32LittleEndian(input[16..], index);
        return new Guid(SHA256.HashData(input)[..16]);
    }
}
