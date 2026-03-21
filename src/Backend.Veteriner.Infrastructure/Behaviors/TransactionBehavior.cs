using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using System;

namespace Backend.Veteriner.Application.Common.Behaviors;

/// <summary>
/// Transactional command'leri tek DB transaction içinde çalıştırır.
/// 
/// Notlar:
/// - Commit işlemi handler içinde _uow.SaveChangesAsync(ct) ile yapılır.
/// - Bu behavior sadece transaction boundary sağlar.
/// - Query'ler transaction açmaz.
/// - Validation başarısızsa transaction hiç başlamaz.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly AppDbContext _db;
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;

    public TransactionBehavior(
        AppDbContext db,
        ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        // Sadece transactional command'lerde transaction aç
        if (request is not ITransactionalRequest)
            return await next();

        // Zaten açık transaction varsa nested transaction açma
        if (_db.Database.CurrentTransaction is not null)
            return await next();

        var requestName = typeof(TRequest).Name;

        await using IDbContextTransaction tx = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            _logger.LogInformation("TRANSACTION START {RequestName}", requestName);

            var response = await next();

            // Commit burada transaction commit'i yapar.
            // SaveChanges handler içinde UoW ile zaten çağrılmış olmalı.
            await tx.CommitAsync(ct);

            _logger.LogInformation("TRANSACTION COMMIT {RequestName}", requestName);

            return response;
        }
        catch
        {
            await tx.RollbackAsync(ct);

            _logger.LogWarning("TRANSACTION ROLLBACK {RequestName}", requestName);

            throw;
        }
    }
}