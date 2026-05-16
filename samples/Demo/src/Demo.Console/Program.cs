using Demo.Shared;

Console.WriteLine(DemoCatalog.Current.DisplayName);
Console.WriteLine($"Project types: {string.Join(", ", DemoCatalog.ProjectTypes)}");
