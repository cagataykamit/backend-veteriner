using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Projections.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Projections.Payments;

/// <summary>
/// Command DB <c>Payments</c> ve Query DB <c>PaymentReadModels</c> global satır sayılarından
/// <see cref="PaymentReadModelHealthSignal"/> üretir (CQRS-14F). Salt okunur (<c>AsNoTracking</c>).
/// </summary>
public sealed class PaymentReadModelHealthReader : IPaymentReadModelHealthReader
{
    private readonly AppDbContext _commandDb;
    private readonly QueryDbContext _queryDb;
    private readonly QueryReadModelsOptions _queryReadModelsOptions;

    public PaymentReadModelHealthReader(
        AppDbContext commandDb,
        QueryDbContext queryDb,
        IOptions<QueryReadModelsOptions> queryReadModelsOptions)
    {
        _commandDb = commandDb;
        _queryDb = queryDb;
        _queryReadModelsOptions = queryReadModelsOptions.Value;
    }

    public async Task<PaymentReadModelHealthSignal> GetSignalAsync(CancellationToken cancellationToken = default)
    {
        var commandPaymentCount = await _commandDb.Payments.AsNoTracking().LongCountAsync(cancellationToken);
        var readModelCount = await _queryDb.PaymentReadModels.AsNoTracking().LongCountAsync(cancellationToken);

        return new PaymentReadModelHealthSignal(
            commandPaymentCount,
            readModelCount,
            _queryReadModelsOptions.PaymentsListReadEnabled);
    }
}
