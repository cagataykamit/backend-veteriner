using Ardalis.Specification;

namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Ardalis.Specification tabanlı generic write repository sözleşmesi.
/// Aggregate kökleri için CRUD ve spec tabanlı komut operasyonlarını destekler.
/// </summary>
public interface IRepository<T> : IRepositoryBase<T> where T : class { }
