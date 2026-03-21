namespace Backend.Veteriner.Domain.Authorization;

public sealed class Permission
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Code { get; private set; } = default!;
    public string? Description { get; private set; }
    public string? Group { get; private set; }

    private Permission() { }

    public Permission(string code, string? description = null, string? group = null)
    {
        SetCode(code);
        Description = Normalize(description);
        Group = Normalize(group);
    }

    public void UpdateDetails(string? description, string? group)
    {
        Description = Normalize(description);
        Group = Normalize(group);
    }

    public void Rename(string code)
    {
        SetCode(code);
    }

    private void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Permission code boş olamaz.", nameof(code));

        Code = code.Trim();
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
