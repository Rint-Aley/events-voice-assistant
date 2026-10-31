using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using TaskManager.Application.Abstractions;
using TaskManager.Events.Domain;
using TaskManager.Infrastructure.Jobs;
using TaskManager.Infrastructure.Persistence;
using TaskManager.Turns.Application.Clients.Nlu;
using TaskManager.Turns.Application.Clients.Tts;
using TaskManager.Turns.Application.Scenarios;
using TaskManager.Turns.Domain;
using TaskManager.IntegrationTests.Turns;
using Testcontainers.PostgreSql;

namespace TaskManager.IntegrationTests.E2E;

/// <summary>
/// Проверяет retry policy настоящего Quartz (in-memory store) на реальном Postgres.
/// Постоянные 4xx завершают задачу без повтора, временные 5xx повторяются трижды,
/// после чего <see cref="RetryExhaustedListener"/> закрывает ход или озвучку.
/// </summary>
public sealed class QuartzRetryE2ETests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("pgvector/pgvector:0.8.7-pg17").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Nlu_job_5xx_is_retried_three_times_then_turn_fails_with_nlu_failed()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        Guid turnId = await SubmitTextAsync(harness);
        harness.Nlu.Respond = _ => throw new NluException("nlu unavailable", 503);

        await using ServiceProvider provider = BuildProvider(harness);
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        var adapter = CreateAdapter(scheduler);

        try
        {
            // Act
            await adapter.ScheduleAsync(JobKind.Nlu, turnId);
            await WaitForTurnStateAsync(turnId, TurnState.Failed);

            // Assert
            harness.Nlu.Requests.Should().HaveCount(3);

            await using TaskManagerDbContext dbContext = CreateDbContext();
            Turn persisted = await dbContext.Turns.AsNoTracking()
                .SingleAsync(turn => turn.Id == turnId);
            persisted.State.Should().Be(TurnState.Failed);
            persisted.ErrorCode.Should().Be(TurnErrors.NluFailedCode);
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Fact]
    public async Task Nlu_job_4xx_is_not_retried_and_fails_turn_once()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        Guid turnId = await SubmitTextAsync(harness);
        harness.Nlu.Respond = _ => throw new NluException("bad request", 400);

        await using ServiceProvider provider = BuildProvider(harness);
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        var adapter = CreateAdapter(scheduler);

        try
        {
            // Act
            await adapter.ScheduleAsync(JobKind.Nlu, turnId);
            await WaitForTurnStateAsync(turnId, TurnState.Failed);

            // Assert
            harness.Nlu.Requests.Should().ContainSingle();

            await using TaskManagerDbContext dbContext = CreateDbContext();
            Turn persisted = await dbContext.Turns.AsNoTracking()
                .SingleAsync(turn => turn.Id == turnId);
            persisted.ErrorCode.Should().Be(TurnErrors.NluFailedCode);
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Fact]
    public async Task Tts_job_5xx_is_retried_three_times_then_audio_fails_but_turn_stays_completed()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        (Guid turnId, Guid answerId) = await CreateAppliedTaskAsync(harness);
        harness.Tts.Respond = _ => throw new TtsException("tts unavailable", 503);

        await using ServiceProvider provider = BuildProvider(harness);
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        var adapter = CreateAdapter(scheduler);

        try
        {
            // Act
            await adapter.ScheduleAsync(JobKind.Tts, answerId);
            await WaitForAudioStatusAsync(answerId, AnswerAudioStatus.Failed);

            // Assert
            harness.Tts.Requests.Should().HaveCount(3);

            await using TaskManagerDbContext dbContext = CreateDbContext();
            Turn turn = await dbContext.Turns.AsNoTracking()
                .SingleAsync(item => item.Id == turnId);
            turn.State.Should().Be(TurnState.Completed);
            turn.ErrorCode.Should().BeNull();

            Answer answer = await dbContext.Answers.AsNoTracking()
                .SingleAsync(item => item.Id == answerId);
            answer.AudioStatus.Should().Be(AnswerAudioStatus.Failed);
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Fact]
    public async Task Tts_job_4xx_marks_audio_failed_without_retry()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        (Guid turnId, Guid answerId) = await CreateAppliedTaskAsync(harness);
        harness.Tts.Respond = _ => throw new TtsException("bad request", 400);

        await using ServiceProvider provider = BuildProvider(harness);
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        var adapter = CreateAdapter(scheduler);

        try
        {
            // Act
            await adapter.ScheduleAsync(JobKind.Tts, answerId);
            await WaitForAudioStatusAsync(answerId, AnswerAudioStatus.Failed);

            // Assert
            harness.Tts.Requests.Should().ContainSingle();

            await using TaskManagerDbContext dbContext = CreateDbContext();
            Turn turn = await dbContext.Turns.AsNoTracking()
                .SingleAsync(item => item.Id == turnId);
            turn.State.Should().Be(TurnState.Completed);

            Answer answer = await dbContext.Answers.AsNoTracking()
                .SingleAsync(item => item.Id == answerId);
            answer.AudioStatus.Should().Be(AnswerAudioStatus.Failed);
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    private Task<TurnsIntakeHarness> CreateHarnessAsync() =>
        TurnsIntakeHarness.CreateAsync(_postgres.GetConnectionString(), Now);

    private TaskManagerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TaskManagerDbContext>()
            .UseTaskManagerPostgres(_postgres.GetConnectionString())
            .Options;

        return new TaskManagerDbContext(options);
    }

    private static async Task<Guid> SubmitTextAsync(TurnsIntakeHarness harness)
    {
        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "купи хлеб"));

        submitted.IsSuccess.Should().BeTrue();

        return submitted.Value.TurnId;
    }

    private static async Task<(Guid TurnId, Guid AnswerId)> CreateAppliedTaskAsync(
        TurnsIntakeHarness harness)
    {
        harness.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "create_task",
            0.9,
            new NluEntities(Description: "купить хлеб"),
            Embedding: new NluEmbedding(
                EventEmbedding.Model,
                EventEmbedding.Dimensions,
                Enumerable.Repeat(0.1, EventEmbedding.Dimensions).ToArray()));

        Guid turnId = await SubmitTextAsync(harness);

        var interpreted = await harness.Interpret.HandleAsync(turnId);
        interpreted.IsSuccess.Should().BeTrue();
        interpreted.Value.State.Should().Be(TurnState.Applying);

        var applied = await harness.Apply.HandleAsync(turnId);
        applied.IsSuccess.Should().BeTrue();
        applied.Value.AnswerId.Should().NotBeNull();

        return (turnId, applied.Value.AnswerId!.Value);
    }

    private static ServiceProvider BuildProvider(TurnsIntakeHarness harness)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(harness.Interpret);
        services.AddSingleton(harness.Synthesize);
        services.AddSingleton(harness.FinalizeFailure);

        services.AddQuartz(quartz =>
        {
            quartz.UseInMemoryStore(_ => { });
            quartz.AddJobType<NluJob>();
            quartz.AddJobType<TtsJob>();
            quartz.AddTriggerListener<RetryExhaustedListener>(
                Array.Empty<IMatcher<TriggerKey>>());
        });

        return services.BuildServiceProvider();
    }

    private static QuartzJobScheduler CreateAdapter(IScheduler scheduler) =>
        new(
            scheduler,
            new QuartzJobSchedulerOptions
            {
                MaxAttempts = 3,
                RetryDelay = TimeSpan.FromMilliseconds(100),
            });

    private async Task WaitForTurnStateAsync(Guid turnId, TurnState expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (true)
        {
            await using TaskManagerDbContext dbContext = CreateDbContext();
            TurnState state = await dbContext.Turns.AsNoTracking()
                .Where(turn => turn.Id == turnId)
                .Select(turn => turn.State)
                .SingleAsync(timeout.Token);

            if (state == expected)
            {
                return;
            }

            await Task.Delay(50, timeout.Token);
        }
    }

    private async Task WaitForAudioStatusAsync(Guid answerId, AnswerAudioStatus expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (true)
        {
            await using TaskManagerDbContext dbContext = CreateDbContext();
            AnswerAudioStatus status = await dbContext.Answers.AsNoTracking()
                .Where(answer => answer.Id == answerId)
                .Select(answer => answer.AudioStatus)
                .SingleAsync(timeout.Token);

            if (status == expected)
            {
                return;
            }

            await Task.Delay(50, timeout.Token);
        }
    }
}
