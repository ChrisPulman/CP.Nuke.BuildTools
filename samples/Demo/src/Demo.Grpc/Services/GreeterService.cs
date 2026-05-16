using Demo.Shared;
using Grpc.Core;

namespace Demo.Grpc.Services;

public sealed class GreeterService : Greeter.GreeterBase
{
    public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
    {
        var name = string.IsNullOrWhiteSpace(request.Name) ? DemoCatalog.Current.Name : request.Name;
        return Task.FromResult(new HelloReply { Message = $"Hello {name}" });
    }
}
