namespace Backend.Veteriner.Domain.Auth;

public sealed class OperationClaim
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = default!; // �rn: "Admin", "Editor"

    private OperationClaim() { }

    public OperationClaim(string name)
    {
        Name = name.Trim();
    }

    public void Rename(string name)
    {
        Name = name.Trim();
    }
}
