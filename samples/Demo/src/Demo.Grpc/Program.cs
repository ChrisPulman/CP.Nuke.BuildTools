using Demo.Grpc.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<GreeterService>();
app.MapGet("/", () => "Use a gRPC client to call Demo.Grpc.Greeter/SayHello.");

app.Run();
