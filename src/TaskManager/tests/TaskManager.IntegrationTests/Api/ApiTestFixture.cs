using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Quartz;
using TaskManager.Application.Abstractions;
using TaskManager.Infrastructure.Persistence;
using TaskManager.IntegrationTests.Turns;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Ports;
using Testcontainers.PostgreSql;

namespace TaskManager.IntegrationTests.Api;

/// <summary>
/// Поднимает TaskManager.Api на реальном Postgres. STT/NLU/TTS/S3/Quartz заменены
/// заглушками: тесты проверяют HTTP-контракт и SSE, а не внешние сервисы.
/// </summary>
public sealed class ApiTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("pgvector/pgvector:0.8.7-pg17").Build();

    public FakeNluClient Nlu { get; } = new();

    public FakeTtsClient Tts { get; } = new();

    public FakeSttClient Stt { get; } = new();

    public FakeObjectStorage Storage { get; } = new();

    public FakeJobScheduler Scheduler { get; } = new();

    public TestClock Clock { get; } =
        new(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);

    public TaskManagerApiFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Factory = new TaskManagerApiFactory(
            _postgres.GetConnectionString(),
            Nlu,
            Tts,
            Stt,
            Storage,
            Scheduler,
            Clock);

        // Первый клиент запускает хост; после этого схема создаётся миграциями.
        using HttpClient client = Factory.CreateClient();
        using IServiceScope scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskManagerDbContext>();
        await dbContext.Database.MigrateAsync();

        await ResetAsync();
    }

    public async Task ResetAsync()
    {
        Scheduler.Reset();
        Nlu.Reset();
        Tts.Reset();
        Stt.Reset();
        Storage.Reset();

        using IServiceScope scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskManagerDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE answer_events, answers, domain_commands, drafts, events, turns CASCADE");
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

/// <summary>Фабрика Api с подменой внешних адаптеров на fake-реализации.</summary>
public sealed class TaskManagerApiFactory(
    string connectionString,
    FakeNluClient nlu,
    FakeTtsClient tts,
    FakeSttClient stt,
    FakeObjectStorage storage,
    FakeJobScheduler scheduler,
    TestClock clock) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Настройки задаются настройками хоста, а не переменными окружения: они не
        // должны утекать в другие тестовые коллекции, которые идут параллельно.
        foreach ((string key, string value) in new Dictionary<string, string>
                 {
                     ["TASKMANAGER_CONNECTION_STRING"] = connectionString,
                     ["TIMEZONE"] = "UTC",
                     ["SESSION_ID"] = "00000000-0000-0000-0000-000000000001",
                     // Ждём аудио дольше, чем живёт тест: закрытие потока проверяется отдельно.
                     ["SSE_AUDIO_WAIT_SECONDS"] = "60",
                     ["SSE_PING_SECONDS"] = "1",
                     ["SSE_POLL_INTERVAL_MS"] = "25",
                 })
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            // Quartz в тестах не стартует: планировщик заменён фейком, а фоновые задачи
            // (apply/reconciler) не должны трогать общую тестовую базу.
            foreach (ServiceDescriptor hosted in services
                .Where(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService)
                    && (descriptor.ImplementationType == typeof(QuartzHostedService)
                        || descriptor.ImplementationFactory is not null))
                .ToArray())
            {
                services.Remove(hosted);
            }

            services.RemoveAll<INluClient>();
            services.AddSingleton<INluClient>(nlu);

            services.RemoveAll<ITtsClient>();
            services.AddSingleton<ITtsClient>(tts);

            services.RemoveAll<ISttClient>();
            services.AddSingleton<ISttClient>(stt);

            services.RemoveAll<IObjectStorage>();
            services.AddSingleton<IObjectStorage>(storage);

            services.RemoveAll<IJobScheduler>();
            services.AddSingleton<IJobScheduler>(scheduler);

            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(clock);
        });
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiTestFixture>
{
    public const string Name = "TaskManager.Api";
}
