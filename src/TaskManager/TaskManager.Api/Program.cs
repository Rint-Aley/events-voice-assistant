using TaskManager.Api.Configuration;
using TaskManager.Api.Endpoints;
using TaskManager.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTaskManagerApi(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<ApiExceptionMiddleware>();

var api = app.MapGroup("/api/v1");
UploadsEndpoints.Map(api);
MessagesEndpoints.Map(api);
TurnsEndpoints.Map(api);
HealthEndpoints.Map(api);

app.Run();

/// <summary>Маркер сборки для <c>WebApplicationFactory</c> в интеграционных тестах.</summary>
public partial class Program;
