namespace Backend.Veteriner.Domain.Shared;

/// <param name="Extensions">Yanıt <c>ProblemDetails.extensions</c> alanına eklenen ek bilgi (örn. <c>batchId</c>); çoğu hatada yok.</param>
public readonly record struct Error(string Code, string Message, IReadOnlyDictionary<string, object?>? Extensions = null)
{
    public static readonly Error None = new("", "");
}
