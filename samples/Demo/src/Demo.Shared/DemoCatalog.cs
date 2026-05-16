namespace Demo.Shared;

public static class DemoCatalog
{
    public static DemoProfile Current { get; } =
        new("CP.Nuke.BuildTools demo", "local", new DateOnly(2026, 5, 16));

    public static IReadOnlyList<string> ProjectTypes { get; } =
    [
        "Console",
        "ASP.NET Web API",
        "Blazor",
        "Worker",
        "gRPC",
        "WPF",
        "WinForms",
        "Avalonia",
        "MAUI Windows"
    ];
}
