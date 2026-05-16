namespace Demo.Shared;

public sealed record DemoProfile(string Name, string Environment, DateOnly ReleaseDate)
{
    public string DisplayName => $"{Name} ({Environment})";
}
