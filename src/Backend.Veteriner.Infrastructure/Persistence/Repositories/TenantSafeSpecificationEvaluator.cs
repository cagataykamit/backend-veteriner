using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories;

/// <summary>
/// Ardalis varsayılan değerlendiricisi; tek fark spec'teki <c>IgnoreQueryFilters</c> yalnız
/// <see cref="QueryFilterNames.Ignorable"/> filtrelerini kaldırır, kiracı filtresini kaldırmaz (ADR-040).
/// </summary>
public sealed class TenantSafeSpecificationEvaluator : SpecificationEvaluator
{
    public static TenantSafeSpecificationEvaluator Instance { get; } = new();

    private TenantSafeSpecificationEvaluator()
    {
        var index = Evaluators.FindIndex(e => e is IgnoreQueryFiltersEvaluator);
        if (index < 0)
            throw new InvalidOperationException("Ardalis IgnoreQueryFiltersEvaluator bulunamadı.");
        Evaluators[index] = new IgnorableQueryFiltersEvaluator();
    }

    private sealed class IgnorableQueryFiltersEvaluator : IEvaluator
    {
        public bool IsCriteriaEvaluator => true;

        public IQueryable<T> GetQuery<T>(IQueryable<T> query, ISpecification<T> specification) where T : class
            => specification.IgnoreQueryFilters ? query.IgnoreQueryFilters(QueryFilterNames.Ignorable) : query;
    }
}
