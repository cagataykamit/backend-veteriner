using Backend.Veteriner.Application.SalesPartners.Contracts;
using Mediator;

namespace Backend.Veteriner.Application.SalesPartners.Queries.ValidateCode;

/// <summary>
/// Anonim kod doğrulama (ADR-037 madde 5): yalnız geçerli/geçersiz, gösterilecek indirim yüzdesi ve süre. Geçersiz her durumda
/// aynı yanıt; ortak bilgisi dönmez. Kendi kodunu kullanma kontrolü kiracı/e-posta gerektirdiği için kayıtta yapılır.
/// </summary>
public sealed record ValidateDiscountCodeQuery(string? Code) : IRequest<DiscountCodeValidationDto>;

public sealed class ValidateDiscountCodeQueryHandler : IRequestHandler<ValidateDiscountCodeQuery, DiscountCodeValidationDto>
{
    private readonly DiscountCodeRedemptionService _discountCodes;
    private readonly TimeProvider _timeProvider;

    public ValidateDiscountCodeQueryHandler(DiscountCodeRedemptionService discountCodes, TimeProvider timeProvider)
    {
        _discountCodes = discountCodes;
        _timeProvider = timeProvider;
    }

    public async ValueTask<DiscountCodeValidationDto> Handle(ValidateDiscountCodeQuery request, CancellationToken ct)
    {
        var code = await _discountCodes.FindRedeemableAsync(request.Code, _timeProvider.GetUtcNow().UtcDateTime, ct);
        return code is null ? DiscountCodeValidationDto.Invalid : DiscountCodeValidationDto.From(code);
    }
}
