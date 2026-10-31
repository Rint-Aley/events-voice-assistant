using Microsoft.EntityFrameworkCore;
using TaskManager.Application.Abstractions;
using TaskManager.Turns.Application.Scenarios;
using TaskManager.Turns.Domain;
using Testcontainers.PostgreSql;

namespace TaskManager.IntegrationTests.Turns;

public sealed class ReconcilerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("pgvector/pgvector:0.8.7-pg17").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Reconcile_schedules_missing_nlu_job()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "создай задачу"));

        await harness.Scheduler.RemoveAsync(JobKind.Nlu, submitted.Value.TurnId);
        (await harness.Scheduler.ExistsAsync(JobKind.Nlu, submitted.Value.TurnId))
            .Should().BeFalse();

        // Act
        ReconcileResult result = await harness.Reconcile.HandleAsync();

        // Assert
        result.ScheduledJobs.Should().Be(1);
        (await harness.Scheduler.ExistsAsync(JobKind.Nlu, submitted.Value.TurnId))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Reconcile_expires_overdue_turn()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        Turn turn = Turn.Create(
            Guid.NewGuid(),
            TurnState.Interpreting,
            Guid.NewGuid(),
            deadlineAt: Now.AddMinutes(-1),
            now: Now.AddMinutes(-2),
            text: "просроченный запрос").Value;
        await harness.DbContext.Turns.AddAsync(turn);
        await harness.DbContext.SaveChangesAsync();

        // Act
        ReconcileResult result = await harness.Reconcile.HandleAsync();

        // Assert
        result.ExpiredTurns.Should().Be(1);

        var stored = await harness.DbContext.Turns.AsNoTracking()
            .SingleAsync(item => item.Id == turn.Id);
        stored.State.Should().Be(TurnState.Expired);
        stored.ErrorCode.Should().Be(TurnErrors.DeadlineExceededCode);
    }

    [Fact]
    public async Task Reconcile_schedules_missing_stt_job()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        var submitted = await harness.SubmitVoice.HandleAsync(
            new SubmitVoiceCommand(Guid.NewGuid(), Guid.NewGuid(), "audio/voice.webm"));

        await harness.Scheduler.RemoveAsync(JobKind.Stt, submitted.Value.TurnId);

        // Act
        ReconcileResult result = await harness.Reconcile.HandleAsync();

        // Assert
        result.ScheduledJobs.Should().Be(1);
        (await harness.Scheduler.ExistsAsync(JobKind.Stt, submitted.Value.TurnId))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Reconcile_leaves_turn_within_deadline_untouched()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "создай задачу"));

        // Act
        ReconcileResult result = await harness.Reconcile.HandleAsync();

        // Assert
        result.ExpiredTurns.Should().Be(0);
        result.ScheduledJobs.Should().Be(0);

        var stored = await harness.DbContext.Turns.AsNoTracking()
            .SingleAsync(item => item.Id == submitted.Value.TurnId);
        stored.State.Should().Be(TurnState.Interpreting);
    }

    [Fact]
    public async Task Reconcile_is_idempotent_on_second_pass()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        Turn turn = Turn.Create(
            Guid.NewGuid(),
            TurnState.Interpreting,
            Guid.NewGuid(),
            deadlineAt: Now.AddMinutes(-1),
            now: Now.AddMinutes(-2),
            text: "просроченный запрос").Value;
        await harness.DbContext.Turns.AddAsync(turn);
        await harness.DbContext.SaveChangesAsync();

        // Act
        ReconcileResult first = await harness.Reconcile.HandleAsync();
        ReconcileResult second = await harness.Reconcile.HandleAsync();

        // Assert
        // Второй проход видит только фактические изменения, а не повторные попытки.
        first.ExpiredTurns.Should().Be(1);
        second.Should().Be(new ReconcileResult(0, 0, 0, 0));
    }

    [Fact]
    public async Task Reconcile_keeps_audio_pending_when_tts_job_exists()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        Turn turn = Turn.Restore(
            Guid.NewGuid(),
            Guid.NewGuid(),
            TurnState.Completed,
            Turn.InitialStateVersion + 1,
            Guid.NewGuid(),
            deadlineAt: Now.AddMinutes(5),
            createdAt: Now.AddMinutes(-3),
            updatedAt: Now.AddMinutes(-2),
            text: "готовый текст").Value;
        Answer answer = Answer.Create(turn.Id, "ответ", Now.AddMinutes(-2)).Value;

        await harness.DbContext.Turns.AddAsync(turn);
        await harness.DbContext.Answers.AddAsync(answer);
        await harness.DbContext.SaveChangesAsync();
        await harness.Scheduler.ScheduleAsync(JobKind.Tts, answer.Id);

        // Act
        ReconcileResult result = await harness.Reconcile.HandleAsync();

        // Assert
        result.FailedAudio.Should().Be(0);

        var stored = await harness.DbContext.Answers.AsNoTracking()
            .SingleAsync(item => item.Id == answer.Id);
        stored.AudioStatus.Should().Be(AnswerAudioStatus.Pending);
    }

    [Fact]
    public async Task Reconcile_fails_stale_pending_audio_without_task()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        Turn turn = Turn.Restore(
            Guid.NewGuid(),
            Guid.NewGuid(),
            TurnState.Completed,
            Turn.InitialStateVersion + 1,
            Guid.NewGuid(),
            deadlineAt: Now.AddMinutes(5),
            createdAt: Now.AddMinutes(-3),
            updatedAt: Now.AddMinutes(-2),
            text: "готовый текст").Value;
        Answer answer = Answer.Create(turn.Id, "ответ", Now.AddMinutes(-2)).Value;

        await harness.DbContext.Turns.AddAsync(turn);
        await harness.DbContext.Answers.AddAsync(answer);
        await harness.DbContext.SaveChangesAsync();

        // Act
        ReconcileResult result = await harness.Reconcile.HandleAsync();

        // Assert
        result.FailedAudio.Should().Be(1);

        var stored = await harness.DbContext.Answers.AsNoTracking()
            .SingleAsync(item => item.Id == answer.Id);
        stored.AudioStatus.Should().Be(AnswerAudioStatus.Failed);

        var storedTurn = await harness.DbContext.Turns.AsNoTracking()
            .SingleAsync(item => item.Id == turn.Id);
        storedTurn.State.Should().Be(TurnState.Completed);
    }

    [Fact]
    public async Task Reconcile_fails_applying_turn_when_all_commands_failed()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        Turn turn = Turn.Restore(
            Guid.NewGuid(),
            Guid.NewGuid(),
            TurnState.Applying,
            Turn.InitialStateVersion + 2,
            Guid.NewGuid(),
            deadlineAt: Now.AddMinutes(5),
            createdAt: Now.AddMinutes(-3),
            updatedAt: Now.AddMinutes(-2),
            text: "создай задачу").Value;

        DomainCommand command = DomainCommand.Create(
            turn.Id,
            seq: 0,
            DomainCommandOperation.EventCreate,
            "{}",
            Now.AddMinutes(-2)).Value;
        command.RegisterFailure(
            "domain failed",
            Now.AddMinutes(-1),
            Now.AddMinutes(-1),
            maxAttempts: 1);

        await harness.DbContext.Turns.AddAsync(turn);
        await harness.DbContext.DomainCommands.AddAsync(command);
        await harness.DbContext.SaveChangesAsync();

        // Act
        ReconcileResult result = await harness.Reconcile.HandleAsync();

        // Assert
        result.ApplyFailedTurns.Should().Be(1);

        var stored = await harness.DbContext.Turns.AsNoTracking()
            .SingleAsync(item => item.Id == turn.Id);
        stored.State.Should().Be(TurnState.Failed);
        stored.ErrorCode.Should().Be(TurnErrors.DomainApplyFailedCode);
    }

    private Task<TurnsIntakeHarness> CreateHarnessAsync() =>
        TurnsIntakeHarness.CreateAsync(_postgres.GetConnectionString(), Now);
}
