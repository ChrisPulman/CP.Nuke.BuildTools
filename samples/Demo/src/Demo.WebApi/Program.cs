using Demo.Shared;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Results.Ok(DemoCatalog.Current));
app.MapGet("/project-types", () => Results.Ok(DemoCatalog.ProjectTypes));

app.Run();
