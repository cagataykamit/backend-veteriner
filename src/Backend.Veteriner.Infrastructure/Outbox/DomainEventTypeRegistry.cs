using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Infrastructure.Outbox;

/// <summary>
/// Domain event CLR tiplerini adlarına göre resolve eder.
/// Outbox worker deserialize sırasında kullanır.
/// </summary>
public sealed class DomainEventTypeRegistry
{
    private readonly Dictionary<string, Type> _types;

    public DomainEventTypeRegistry()
    {
        _types = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            })
            .Where(t => typeof(IDomainEvent).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .ToDictionary(t => t.FullName!, t => t);
    }

    public Type? Resolve(string typeName)
        => _types.TryGetValue(typeName, out var type) ? type : null;
}