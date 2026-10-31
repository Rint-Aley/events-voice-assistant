using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Quartz;
using TaskManager.Application.Abstractions;
using TaskManager.Infrastructure.Jobs;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Clients.Stt;
using TaskManager.Turns.Application.Configuration;
using TaskManager.Turns.Application.Scenarios;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Domain;
using TaskManager.IntegrationTests.Turns;
using Testcontainers.PostgreSql;

namespace TaskManager.IntegrationTests.Infrastructure;

public sealed class QuartzJobsTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ScheduleAsync_twice_creates_one_task_with_deterministic_key()
    {
        // Arrange
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(quartz => quartz.UseInMemoryStore(_ => { }))
            .BuildScheduler();
        var adapter = new QuartzJobScheduler(
            scheduler,
            new QuartzJobSchedulerOptions
            {
                MaxAttempts = 3,
                RetryDelay = TimeSpan.FromMilliseconds(100),
            });
        Guid turnId = Guid.NewGuid();

        try
        {
            // Act
            await adapter.ScheduleAsync(JobKind.Stt, turnId);
            await adapter.ScheduleAsync(JobKind.Stt, turnId);

            // Assert
            (await adapter.ExistsAsync(JobKind.Stt, turnId)).Should().BeTrue();

            var jobs = await scheduler.QueryJobs(
                new JobQuery { Group = GroupMatcher<JobKey>.GroupEquals("turns") });
            jobs.Items.Should().ContainSingle();
            jobs.Items[0].Key.Should().Be(new JobKey($"stt:{turnId}", "turns"));
            jobs.Items[0].RequestsRecovery.Should().BeTrue();

            var triggers = await scheduler.QueryTriggers(
                new TriggerQuery { Group = GroupMatcher<TriggerKey>.GroupEquals("turns") });
            triggers.Items.Should().ContainSingle();
            triggers.Items[0].Key.Should().Be(new TriggerKey($"stt:{turnId}", "turns"));
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Fact]
    public async Task ScheduleAsync_with_persistent_store_accepts_guid_target()
    {
        // Arrange
        await using PostgreSqlContainer postgres =
            new PostgreSqlBuilder("pgvector/pgvector:0.8.7-pg17").Build();
        await postgres.StartAsync();

        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(quartz =>
            {
                quartz.UsePersistentStore(store =>
                {
                    store.UsePostgres(postgres.GetConnectionString());
                    store.ConfigureStore(jobStore =>
                    {
                        jobStore.TablePrefix = "qrtz_";
                        jobStore.StoreJobDataAsStrings = true;
                    });
                    store.ProvisionSchema();
                });
            })
            .BuildScheduler();
        var adapter = new QuartzJobScheduler(
            scheduler,
            new QuartzJobSchedulerOptions
            {
                MaxAttempts = 3,
                RetryDelay = TimeSpan.FromMilliseconds(100),
            });
        Guid turnId = Guid.NewGuid();

        try
        {
            // Act
            await adapter.ScheduleAsync(JobKind.Nlu, turnId);

            // Assert
            (await adapter.ExistsAsync(JobKind.Nlu, turnId)).Should().BeTrue();
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Fact]
    public async Task SttJob_permanent_4xx_is_not_retried_and_fails_turn()
    {
        // Arrange
        Turn turn = CreateTranscribingTurn();
        var stt = new ScriptedSttClient(_ => throw new SttException("bad request", 400));
        await using ServiceProvider provider = BuildProvider(turn, stt);

        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>()
            .GetScheduler();
        await scheduler.Start();

        var adapter = new QuartzJobScheduler(
            scheduler,
            new QuartzJobSchedulerOptions
            {
                MaxAttempts = 3,
                RetryDelay = TimeSpan.FromMilliseconds(100),
            });

        try
        {
            // Act
            await adapter.ScheduleAsync(JobKind.Stt, turn.Id);
            await WaitUntilAsync(() => turn.State == TurnState.Failed);

            // Assert
            stt.Calls.Should().Be(1);
            turn.State.Should().Be(TurnState.Failed);
            turn.ErrorCode.Should().Be(TurnErrors.SttFailedCode);
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Fact]
    public async Task SttJob_5xx_is_retried_and_exhaustion_finalizes_turn()
    {
        // Arrange
        Turn turn = CreateTranscribingTurn();
        var stt = new ScriptedSttClient(_ => throw new SttException("unavailable", 503));
        await using ServiceProvider provider = BuildProvider(turn, stt);

        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>()
            .GetScheduler();
        await scheduler.Start();

        var adapter = new QuartzJobScheduler(
            scheduler,
            new QuartzJobSchedulerOptions
            {
                MaxAttempts = 3,
                RetryDelay = TimeSpan.FromMilliseconds(100),
            });

        try
        {
            // Act
            await adapter.ScheduleAsync(JobKind.Stt, turn.Id);
            await WaitUntilAsync(() => turn.State == TurnState.Failed);

            // Assert
            stt.Calls.Should().Be(3);
            turn.State.Should().Be(TurnState.Failed);
            turn.ErrorCode.Should().Be(TurnErrors.SttFailedCode);
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Fact]
    public async Task RetryExhaustedListener_tts_failure_marks_audio_failed()
    {
        // Arrange
        Guid turnId = Guid.NewGuid();
        Guid answerId = Guid.NewGuid();
        Answer answer = Answer.Restore(
            answerId,
            turnId,
            "готовый ответ",
            AnswerAudioStatus.Pending,
            Now).Value;

        var answers = Substitute.For<IAnswerRepository>();
        answers.GetByIdForUpdateAsync(answerId, Arg.Any<CancellationToken>()).Returns(answer);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IUnitOfWork>());
        services.AddSingleton(Substitute.For<ITurnRepository>());
        services.AddSingleton<IClock>(new TestClock(Now));
        services.AddSingleton(answers);
        services.AddScoped<FailTurnHandler>();
        services.AddScoped<MarkAnswerAudioFailedHandler>();
        services.AddScoped<FinalizeJobFailureHandler>();
        services.AddSingleton<RetryExhaustedListener>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        var listener = provider.GetRequiredService<RetryExhaustedListener>();

        var context = Substitute.For<IJobExecutionContext>();
        context.MergedJobDataMap.Returns(new JobDataMap
        {
            ["target_id"] = answerId.ToString("D"),
            ["kind"] = JobKind.Tts.ToString(),
        });

        // Act
        await listener.TriggerRetriesExhausted(
            Substitute.For<ITrigger>(),
            context,
            new JobExecutionException(new InvalidOperationException("tts failed")));

        // Assert
        answer.AudioStatus.Should().Be(AnswerAudioStatus.Failed);
    }

    [Theory]
    [InlineData(JobKind.Stt, TurnState.Transcribing, TurnErrors.SttFailedCode)]
    [InlineData(JobKind.Nlu, TurnState.Interpreting, TurnErrors.NluFailedCode)]
    public async Task RetryExhaustedListener_stt_and_nlu_fail_turn(
        JobKind kind,
        TurnState state,
        string expectedCode)
    {
        // Arrange
        Turn turn = CreateTurn(state);

        var turns = Substitute.For<ITurnRepository>();
        turns.GetByIdForUpdateAsync(turn.Id, Arg.Any<CancellationToken>()).Returns(turn);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IUnitOfWork>());
        services.AddSingleton(turns);
        services.AddSingleton(Substitute.For<IAnswerRepository>());
        services.AddSingleton<IClock>(new TestClock(Now));
        services.AddScoped<FailTurnHandler>();
        services.AddScoped<MarkAnswerAudioFailedHandler>();
        services.AddScoped<FinalizeJobFailureHandler>();
        services.AddSingleton<RetryExhaustedListener>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        var listener = provider.GetRequiredService<RetryExhaustedListener>();

        var context = Substitute.For<IJobExecutionContext>();
        context.MergedJobDataMap.Returns(new JobDataMap
        {
            ["target_id"] = turn.Id.ToString("D"),
            ["kind"] = kind.ToString(),
        });

        // Act
        await listener.TriggerRetriesExhausted(
            Substitute.For<ITrigger>(),
            context,
            new JobExecutionException(new InvalidOperationException("service failed")));

        // Assert
        turn.State.Should().Be(TurnState.Failed);
        turn.ErrorCode.Should().Be(expectedCode);
    }

    private static ServiceProvider BuildProvider(Turn turn, ScriptedSttClient stt)
    {
        var turns = Substitute.For<ITurnRepository>();
        turns.GetByIdAsync(turn.Id, Arg.Any<CancellationToken>()).Returns(turn);
        turns.GetByIdForUpdateAsync(turn.Id, Arg.Any<CancellationToken>()).Returns(turn);

        var storage = Substitute.For<IObjectStorage>();
        storage.CreatePresignedDownloadUrlAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new Uri("http://s3.local/audio/test.webm"));
        storage.GetContentType(Arg.Any<string>()).Returns("audio/webm");

        var scheduler = Substitute.For<IJobScheduler>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var clock = new TestClock(Now);

        var handleResult = new HandleSttResultHandler(turns, scheduler, unitOfWork, clock);
        var transcribe = new TranscribeTurnHandler(
            stt,
            storage,
            turns,
            handleResult,
            new SttTurnOptions("ru"));
        var failTurn = new FailTurnHandler(turns, unitOfWork, clock);
        var failAudio = new MarkAnswerAudioFailedHandler(
            Substitute.For<IAnswerRepository>(),
            unitOfWork);
        var finalizer = new FinalizeJobFailureHandler(failTurn, failAudio);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => transcribe);
        services.AddScoped(_ => finalizer);
        services.AddScoped<SttJob>();

        services.AddQuartz(quartz =>
        {
            quartz.UseInMemoryStore(_ => { });
            quartz.AddJobType<SttJob>();
            quartz.AddTriggerListener<RetryExhaustedListener>(
                Array.Empty<IMatcher<TriggerKey>>());
        });

        return services.BuildServiceProvider();
    }

    private static Turn CreateTranscribingTurn()
    {
        return Turn.Create(
            Guid.NewGuid(),
            TurnState.Transcribing,
            Guid.NewGuid(),
            Now.AddMinutes(5),
            Now,
            audioObjectKey: "audio/test.webm").Value;
    }

    private static Turn CreateTurn(TurnState state) =>
        state == TurnState.Transcribing
            ? CreateTranscribingTurn()
            : Turn.Create(
                Guid.NewGuid(),
                state,
                Guid.NewGuid(),
                Now.AddMinutes(5),
                Now,
                text: "создай задачу").Value;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!condition())
        {
            await Task.Delay(25, timeout.Token);
        }
    }

    private sealed class ScriptedSttClient : ISttClient
    {
        private readonly Func<SttRequest, SttResult> _respond;

        public ScriptedSttClient(Func<SttRequest, SttResult> respond)
        {
            _respond = respond;
        }

        public int Calls { get; private set; }

        public Task<SttResult> TranscribeAsync(
            SttRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_respond(request));
        }
    }
}
