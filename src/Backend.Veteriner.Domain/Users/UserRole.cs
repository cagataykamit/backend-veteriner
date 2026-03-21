namespace Backend.Veteriner.Domain.Users;

public class UserRole
{
    public int Id { get; private set; }
    public string Name { get; private set; } = default!;

    private UserRole() { } // EF Core i�in gereklidir (reflection ile yarat�r)

    public UserRole(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Role ismi bo� olamaz.", nameof(name));

        Name = name.Trim();
    }

    public override bool Equals(object? obj)
        => obj is UserRole other && Name.Equals(other.Name, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode()
        => Name.ToLowerInvariant().GetHashCode();
}
