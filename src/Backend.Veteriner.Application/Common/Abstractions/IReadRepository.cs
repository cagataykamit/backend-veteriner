using Ardalis.Specification;

namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Ardalis.Specification tabanlı generic read repository sözleşmesi.
/// Spec tabanlı query operasyonları için standart giriş noktasıdır.
/// </summary>
public interface IReadRepository<T> : IReadRepositoryBase<T> where T : class { }
