using ImageStore.Api.Hosting;
using ImageStore.Application;
using ImageStore.Infrastructure;
using ImageStore.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

var app = builder.Build();

app.EnsureStorageIsOutsideWebRoot();
await app.Services.InitializeDatabaseAsync();

app.UseApiPipeline();

await app.RunAsync();

/// <summary>Entry point; public so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program;
