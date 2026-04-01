namespace Backend.Veteriner.Application.Payments.Queries.GetList;

/// <summary>
/// Ödeme listesi sayfalama. Metin araması ayrı HTTP query parametresi <c>search</c> ile verilir.
/// <c>sort</c>/<c>order</c> bu endpointte yoktur.
/// </summary>
public sealed class PaymentListPagingRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
