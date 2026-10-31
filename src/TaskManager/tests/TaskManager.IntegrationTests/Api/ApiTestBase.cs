using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.Api.Contracts;
using TaskManager.Infrastructure.Persistence;

namespace TaskManager.IntegrationTests.Api;

/// <summary>Общая обвязка API-тестов: чистый ход каждой БД и готовый HttpClient.</summary>
[Collection(ApiCollection.Name)]
public abstract class ApiTestBase : IAsyncLifetime
{
    protected ApiTestBase(ApiTestFixture fixture)
    {
        Fixture = fixture;
    }

    protected ApiTestFixture Fixture { get; }

    protected HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Fixture.ResetAsync();
        Client = Fixture.Factory.CreateClient();
        Client.Timeout = TimeSpan.FromSeconds(10);
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        return Task.CompletedTask;
    }

    protected Task<HttpResponseMessage> PostJsonAsync(string path, object body) =>
        Client.PostAsJsonAsync(path, body, ApiJson.Options);

    protected async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();

        return JsonDocument.Parse(content);
    }

    protected async Task<Guid> CreateTextTurnAsync(
        string text = "создай задачу",
        string? idempotencyKey = null)
    {
        HttpResponseMessage response = await PostJsonAsync(
            "/api/v1/messages",
            new
            {
                idempotency_key = idempotencyKey ?? Guid.NewGuid().ToString(),
                text,
                object_key = (string?)null,
            });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using JsonDocument body = await ReadJsonAsync(response);

        return body.RootElement.GetProperty("turn_id").GetGuid();
    }

    protected async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using AsyncServiceScope scope = Fixture.Factory.Services.CreateAsyncScope();

        return await action(scope.ServiceProvider);
    }

    protected async Task WithScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using AsyncServiceScope scope = Fixture.Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    protected async Task<Guid> GetAnswerIdAsync(Guid turnId) =>
        await WithScopeAsync(async provider =>
        {
            var dbContext = provider.GetRequiredService<TaskManagerDbContext>();

            return await dbContext.Answers
                .Where(answer => answer.TurnId == turnId)
                .Select(answer => answer.Id)
                .SingleAsync();
        });
}
