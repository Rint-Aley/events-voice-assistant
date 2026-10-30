using Microsoft.EntityFrameworkCore;
using TaskManager.Api.Configuration;
using TaskManager.Api.Time;
using TaskManager.Application.Abstractions;
using TaskManager.Events.Application.Ports;
using TaskManager.Events.Application.Scenarios;
using TaskManager.Infrastructure.Clients;
using TaskManager.Infrastructure.Jobs;
using TaskManager.Infrastructure.Persistence;
using TaskManager.Infrastructure.Persistence.Queries;
using TaskManager.Infrastructure.Persistence.Repositories;
using TaskManager.Infrastructure.Storage;
using TaskManager.Infrastructure.Streams;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Configuration;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Application.Scenarios;

namespace TaskManager.Api.Configuration;

/// <summary>
/// Сборка приложения TaskManager: конфигурация, порты Application и их адаптеры.
/// Бизнес-сценарии остаются в Application; Api только вызывает их.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTaskManagerApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = ConnectionStringResolver.Resolve(configuration);

        services.AddDbContext<TaskManagerDbContext>(options =>
            options.UseTaskManagerPostgres(connectionString));

        RegisterPersistence(services);
        RegisterOptions(services, configuration);
        RegisterTurns(services);
        RegisterEvents(services);
        RegisterAdapters(services, configuration, connectionString);

        return services;
    }

    private static void RegisterPersistence(IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<ITurnRepository, TurnRepository>();
        services.AddScoped<IDraftRepository, DraftRepository>();
        services.AddScoped<IAnswerRepository, AnswerRepository>();
        services.AddScoped<IDomainCommandRepository, DomainCommandRepository>();
        services.AddScoped<IEventRepository, EventRepository>();

        services.AddScoped<ITurnQuery, TurnQuery>();
        services.AddScoped<IEventQuery, EventQuery>();
    }

    private static void RegisterOptions(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(_ => TaskManagerOptions.FromConfiguration(configuration));

        services.AddSingleton(sp =>
            new TurnIntakeOptions(sp.GetRequiredService<TaskManagerOptions>().Deadline));

        services.AddSingleton(_ => new SttTurnOptions(configuration["STT_LANGUAGE"] ?? "ru"));

        services.AddSingleton(_ => new TtsOptions(
            configuration["TTS_VOICE"] ?? "default",
            configuration["TTS_FORMAT"] ?? "ogg_opus",
            configuration.GetValue<double?>("TTS_SPEED") ?? 1));

        services.AddSingleton(_ => new SttClientOptions(
            TimeSpan.FromSeconds(configuration.GetValue<int?>("STT_TIMEOUT") ?? 30)));

        services.AddSingleton(_ => new NluClientOptions(
            TimeSpan.FromSeconds(configuration.GetValue<int?>("NLU_TIMEOUT") ?? 20)));

        services.AddSingleton(_ => new TtsClientOptions(
            TimeSpan.FromSeconds(configuration.GetValue<int?>("TTS_TIMEOUT") ?? 20)));
    }

    private static void RegisterTurns(IServiceCollection services)
    {
        services.AddScoped<SubmitTextHandler>();
        services.AddScoped<SubmitVoiceHandler>();
        services.AddScoped<HandleSttResultHandler>();
        services.AddScoped<TranscribeTurnHandler>();
        services.AddScoped<InterpretTurnHandler>();
        services.AddScoped<ApplyTurnPlanHandler>();
        services.AddScoped<SynthesizeAnswerHandler>();
        services.AddScoped<CancelDraftHandler>();
        services.AddScoped<GetTurnSnapshotHandler>();
        services.AddScoped<FailTurnHandler>();
        services.AddScoped<MarkAnswerAudioFailedHandler>();
        services.AddScoped<FinalizeJobFailureHandler>();
        services.AddScoped<ReconcileHandler>();
    }

    private static void RegisterEvents(IServiceCollection services)
    {
        services.AddScoped<CreateEventHandler>();
        services.AddScoped<DetectConflictsHandler>();
        services.AddScoped<ListPeriodHandler>();
    }

    private static void RegisterAdapters(
        IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IClock>(sp =>
            new SystemClock(sp.GetRequiredService<TaskManagerOptions>().TimeZone));
        services.AddSingleton<ITurnStream, InMemoryTurnStream>();
        services.AddSingleton<IObjectStorage>(_ => new ObjectStorage(CreateStorageOptions(configuration)));

        services.AddHttpClient<ISttClient, SttClient>(client =>
            client.BaseAddress = CreateBaseAddress(configuration, "STT_BASE_URL", "http://localhost:8000"));

        services.AddHttpClient<INluClient, NluClient>(client =>
            client.BaseAddress = CreateBaseAddress(configuration, "NLU_BASE_URL", "http://localhost:8001"));

        services.AddHttpClient<ITtsClient, TtsClient>(client =>
            client.BaseAddress = CreateBaseAddress(configuration, "TTS_BASE_URL", "http://localhost:8002"));

        services.AddTaskManagerQuartz(
            connectionString,
            new QuartzJobSchedulerOptions
            {
                MaxAttempts = configuration.GetValue<int?>("JOB_MAX_ATTEMPTS") ?? 3,
                ApplyTickInterval = TimeSpan.FromSeconds(
                    configuration.GetValue<int?>("APPLY_TICK_SECONDS") ?? 1),
                ReconcileInterval = TimeSpan.FromSeconds(
                    configuration.GetValue<int?>("RECONCILE_SECONDS") ?? 10),
            });
    }

    private static ObjectStorageOptions CreateStorageOptions(IConfiguration configuration)
    {
        Uri endpoint = new(configuration["S3_ENDPOINT"] ?? "http://localhost:9000");

        return new ObjectStorageOptions(
            endpoint,
            configuration["S3_ACCESS_KEY"] ?? "todo-s3",
            configuration["S3_SECRET_KEY"] ?? "todo-s3-secret",
            configuration["S3_BUCKET_AUDIO"] ?? "audio",
            configuration["S3_BUCKET_TTS"] ?? "tts",
            TimeSpan.FromSeconds(configuration.GetValue<int?>("S3_PRESIGN_TTL_UPLOAD") ?? 600),
            TimeSpan.FromSeconds(configuration.GetValue<int?>("S3_PRESIGN_TTL_DOWNLOAD") ?? 900),
            configuration["S3_REGION"] ?? "us-east-1");
    }

    private static Uri CreateBaseAddress(
        IConfiguration configuration,
        string key,
        string fallback)
    {
        string value = configuration[key] ?? fallback;

        return value.EndsWith('/') ? new Uri(value) : new Uri($"{value}/");
    }
}
