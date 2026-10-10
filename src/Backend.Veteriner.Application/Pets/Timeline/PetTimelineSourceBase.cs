using System.Linq.Expressions;
using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Access;

namespace Backend.Veteriner.Application.Pets.Timeline;

/// <summary>Kaynak başına tek yer: süzgeç, olay tarihi, id ve satır eşlemesi; sorgu/imleç mantığı ortaktır.</summary>
internal abstract class PetTimelineSourceBase<T> : IPetTimelineSource where T : class
{
    private readonly IReadRepository<T> _repository;

    protected PetTimelineSourceBase(IReadRepository<T> repository) => _repository = repository;

    public abstract PetRecordKind Kind { get; }

    /// <summary>Tenant + hayvan + (includeVoided=false ise) yanlış işaretli/iptal gizleme kuralı.</summary>
    protected abstract Expression<Func<T, bool>> Filter(PetTimelineSourceQuery query);

    protected abstract Expression<Func<T, DateTime>> EventAt { get; }

    protected abstract Expression<Func<T, Guid>> Id { get; }

    protected abstract ISpecificationBuilder<T> ApplyClinicScope(ISpecificationBuilder<T> builder, PetTimelineSourceQuery query);

    protected abstract PetTimelineRow ToRow(T entity);

    public async Task<IReadOnlyList<PetTimelineRow>> ReadAsync(PetTimelineSourceQuery query, CancellationToken ct)
    {
        var spec = new PetTimelineSpec<T>(
            Filter(query),
            builder => ApplyClinicScope(builder, query),
            EventAt,
            Id,
            (int)Kind,
            query);
        var entities = await _repository.ListAsync(spec, ct);
        return entities.Select(ToRow).ToList();
    }
}
