using Microsoft.EntityFrameworkCore;
using TaskManager.Application.Abstractions;
using TaskManager.Events.Domain;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Clients.Nlu;
using TaskManager.Turns.Application.Clients.Tts;
using TaskManager.Turns.Application.Plans;
using TaskManager.Turns.Application.Scenarios;
using TaskManager.Turns.Domain;
using Testcontainers.PostgreSql;

namespace TaskManager.IntegrationTests.Turns;

public sealed class TurnsApplyTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("pgvector/pgvector:0.8.7-pg17").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ApplyTurnPlan_creates_event_answer_and_answer_event()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        harness.Nlu.Respond = _ => CreateTaskResponse("купить хлеб");

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "купи хлеб"));
        await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Act
        var applied = await harness.Apply.HandleAsync(submitted.Value.TurnId);

        // Assert
        applied.IsSuccess.Should().BeTrue();
        applied.Value.State.Should().Be(TurnState.Completed);
        applied.Value.AnswerId.Should().NotBeNull();
        applied.Value.Text.Should().Be("Записала задачу: «купить хлеб».");

        var turn = await harness.DbContext.Turns.AsNoTracking()
            .SingleAsync(item => item.Id == submitted.Value.TurnId);
        turn.State.Should().Be(TurnState.Completed);
        turn.ErrorCode.Should().BeNull();

        var @event = await harness.DbContext.Events.AsNoTracking().SingleAsync();
        @event.Kind.Should().Be(EventKind.Task);
        @event.Description.Should().Be("купить хлеб");
        @event.SourceText.Should().Be("купи хлеб");

        var answer = await harness.DbContext.Answers.AsNoTracking().SingleAsync();
        answer.Text.Should().Be(applied.Value.Text);
        answer.TotalCount.Should().BeNull();
        answer.Warnings.Should().BeEmpty();

        var link = await harness.DbContext.AnswerEvents.AsNoTracking().SingleAsync();
        link.AnswerId.Should().Be(answer.Id);
        link.EventId.Should().Be(@event.Id);
        link.Position.Should().Be(0);

        var command = await harness.DbContext.DomainCommands.AsNoTracking().SingleAsync();
        command.Status.Should().Be(DomainCommandStatus.Applied);
        command.Seq.Should().Be(0);
        command.AppliedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ApplyTurnPlan_fills_warnings_and_text_for_meeting_conflict()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        var day = new DateOnly(2026, 10, 30);
        var time = new TimeOnly(15, 0);

        var existing = Event.Create(
            EventKind.Meeting,
            "созвон",
            startsOn: day,
            startsAt: time).Value;
        await harness.DbContext.Events.AddAsync(existing);
        await harness.DbContext.SaveChangesAsync();

        harness.Nlu.Respond = _ => CreateMeetingResponse("планёрка", day, time);

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "создай встречу"));
        await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Act
        var applied = await harness.Apply.HandleAsync(submitted.Value.TurnId);

        // Assert
        applied.IsSuccess.Should().BeTrue();

        var answer = await harness.DbContext.Answers.AsNoTracking().SingleAsync();
        answer.Warnings.Should().ContainSingle()
            .Which.Should().Be(
                "На это время уже запланирована встреча «созвон». Выбери другое время.");
        answer.Text.Should().Be(
            "Записала встречу: «планёрка» — сегодня, 15:00. "
            + "На это время уже запланирована встреча «созвон». Выбери другое время.");

        (await harness.DbContext.Events.AsNoTracking().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ApplyTurnPlan_repeated_application_does_not_create_duplicates()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        harness.Nlu.Respond = _ => CreateTaskResponse("купить хлеб");

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "купи хлеб"));
        await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        var first = await harness.Apply.HandleAsync(submitted.Value.TurnId);

        // Act
        var second = await harness.Apply.HandleAsync(submitted.Value.TurnId);

        // Assert
        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.Value.State.Should().Be(TurnState.Completed);
        second.Value.AnswerId.Should().Be(first.Value.AnswerId);
        second.Value.Text.Should().Be(first.Value.Text);

        (await harness.DbContext.Events.AsNoTracking().CountAsync()).Should().Be(1);
        (await harness.DbContext.Answers.AsNoTracking().CountAsync()).Should().Be(1);
        (await harness.DbContext.AnswerEvents.AsNoTracking().CountAsync()).Should().Be(1);
        (await harness.DbContext.DomainCommands.AsNoTracking().CountAsync()).Should().Be(1);

        harness.Scheduler.Scheduled
            .Count(job => job.Kind == JobKind.Tts)
            .Should().Be(1);
    }

    [Fact]
    public async Task ListPeriodAnswer_reports_total_count_before_limit()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        var day = new DateOnly(2026, 10, 30);

        for (int index = 1; index <= 4; index++)
        {
            await harness.DbContext.Events.AddAsync(
                Event.Create(EventKind.Task, $"задача {index}", startsOn: day).Value);
        }

        await harness.DbContext.SaveChangesAsync();

        harness.Nlu.Respond = _ => ListPeriodResponse("2026-10-30", "2026-10-30");

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "что у меня сегодня"));

        // Act
        var interpreted = await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Assert
        interpreted.IsSuccess.Should().BeTrue();
        interpreted.Value.State.Should().Be(TurnState.Completed);
        interpreted.Value.AnswerId.Should().NotBeNull();

        var answer = await harness.DbContext.Answers.AsNoTracking().SingleAsync();
        answer.TotalCount.Should().Be(4);
        answer.Text.Should().Be("На сегодня: 4 записи. И ещё 1 запись.");

        (await harness.DbContext.AnswerEvents.AsNoTracking().CountAsync()).Should().Be(3);

        var snapshot = await harness.Snapshot.HandleAsync(submitted.Value.TurnId);
        snapshot.Value.Answer.Should().NotBeNull();
        snapshot.Value.Answer!.TotalCount.Should().Be(4);
        snapshot.Value.Answer.Events.Should().HaveCount(3);
    }

    [Fact]
    public async Task ListPeriodAnswer_task_filter_returns_tasks_and_reminders()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        var day = new DateOnly(2026, 10, 30);

        await harness.DbContext.Events.AddAsync(
            Event.Create(
                EventKind.Reminder,
                "позвонить",
                startsOn: day,
                startsAt: new TimeOnly(10, 0)).Value);
        await harness.DbContext.Events.AddAsync(
            Event.Create(EventKind.Task, "купить хлеб", startsOn: day).Value);
        await harness.DbContext.Events.AddAsync(
            Event.Create(EventKind.Task, "разобрать почту").Value);
        await harness.DbContext.Events.AddAsync(
            Event.Create(
                EventKind.Meeting,
                "планёрка",
                startsOn: day,
                startsAt: new TimeOnly(12, 0)).Value);
        await harness.DbContext.SaveChangesAsync();

        harness.Nlu.Respond = _ => ListPeriodResponse(
            "2026-10-30",
            "2026-10-30",
            typeFilter: "task");

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "какие задачи"));

        // Act
        var interpreted = await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Assert
        interpreted.Value.State.Should().Be(TurnState.Completed);

        var answer = await harness.DbContext.Answers.AsNoTracking().SingleAsync();
        answer.TotalCount.Should().Be(3);

        var linked = await harness.DbContext.AnswerEvents.AsNoTracking()
            .OrderBy(link => link.Position)
            .Select(link => link.EventId)
            .ToListAsync();
        var descriptions = await harness.DbContext.Events.AsNoTracking()
            .Where(@event => linked.Contains(@event.Id))
            .Select(@event => @event.Description)
            .ToListAsync();

        descriptions.Should().BeEquivalentTo("позвонить", "купить хлеб", "разобрать почту");
    }

    [Fact]
    public async Task ListPeriodAnswer_returns_empty_text_and_no_events()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        harness.Nlu.Respond = _ => ListPeriodResponse(
            "2026-10-31",
            "2026-10-31",
            typeFilter: "meeting");

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "какие встречи завтра"));

        // Act
        var interpreted = await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Assert
        interpreted.Value.State.Should().Be(TurnState.Completed);

        var answer = await harness.DbContext.Answers.AsNoTracking().SingleAsync();
        answer.Text.Should().Be("Встреч на завтра нет.");
        answer.TotalCount.Should().Be(0);
        (await harness.DbContext.AnswerEvents.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ListPeriodAnswer_without_period_fails_nlu()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        harness.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "list_period",
            0.9,
            new NluEntities());

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "что у меня"));

        // Act
        var interpreted = await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Assert
        interpreted.IsSuccess.Should().BeTrue();
        interpreted.Value.State.Should().Be(TurnState.Failed);
        interpreted.Value.AnswerId.Should().BeNull();

        var turn = await harness.DbContext.Turns.AsNoTracking()
            .SingleAsync(item => item.Id == submitted.Value.TurnId);
        turn.ErrorCode.Should().Be("NLU_FAILED");

        (await harness.DbContext.Answers.AsNoTracking().CountAsync()).Should().Be(0);
        harness.Scheduler.Scheduled.Should().NotContain(job => job.Kind == JobKind.Tts);
    }

    [Fact]
    public async Task ListPeriodAnswer_with_unknown_type_filter_fails_nlu()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        harness.Nlu.Respond = _ => ListPeriodResponse(
            "2026-10-30",
            "2026-10-30",
            typeFilter: "event");

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "что у меня"));

        // Act
        var interpreted = await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Assert
        interpreted.Value.State.Should().Be(TurnState.Failed);

        var turn = await harness.DbContext.Turns.AsNoTracking()
            .SingleAsync(item => item.Id == submitted.Value.TurnId);
        turn.ErrorCode.Should().Be("NLU_FAILED");
    }

    [Fact]
    public async Task ListPeriodAnswer_with_reversed_period_completes_with_retry_text()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        harness.Nlu.Respond = _ => ListPeriodResponse("2026-11-05", "2026-10-30");

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "что у меня"));

        // Act
        var interpreted = await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Assert
        interpreted.Value.State.Should().Be(TurnState.Completed);

        var answer = await harness.DbContext.Answers.AsNoTracking().SingleAsync();
        answer.Text.Should().Be("Не удалось уточнить. Сформулируй запрос иначе.");
        harness.Scheduler.Scheduled.Should().ContainSingle(job => job.Kind == JobKind.Tts);
    }

    [Fact]
    public async Task ApplyTurnPlan_fails_turn_after_three_unsuccessful_attempts()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        Turn turn = await CreateApplyingTurnAsync(harness);

        // Act
        var first = await harness.Apply.HandleAsync(turn.Id);
        var second = await harness.Apply.HandleAsync(turn.Id);
        var third = await harness.Apply.HandleAsync(turn.Id);

        // Assert
        first.IsFailure.Should().BeTrue();
        first.Error!.Code.Should().Be("DOMAIN_APPLY_FAILED");
        second.IsFailure.Should().BeTrue();
        third.IsFailure.Should().BeTrue();
        third.Error!.Code.Should().Be("DOMAIN_APPLY_FAILED");

        var storedTurn = await harness.DbContext.Turns.AsNoTracking()
            .SingleAsync(item => item.Id == turn.Id);
        storedTurn.State.Should().Be(TurnState.Failed);
        storedTurn.ErrorCode.Should().Be("DOMAIN_APPLY_FAILED");

        var command = await harness.DbContext.DomainCommands.AsNoTracking().SingleAsync();
        command.Status.Should().Be(DomainCommandStatus.Failed);
        command.Attempts.Should().Be(3);
        command.LastError.Should().NotBeNullOrWhiteSpace();

        (await harness.DbContext.Events.AsNoTracking().CountAsync()).Should().Be(0);
        (await harness.DbContext.Answers.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ApplyTurnPlan_closes_pending_commands_when_turn_is_terminal()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        Turn turn = await CreateApplyingTurnAsync(harness);

        turn.Transition(TurnTransition.DeadlineExceeded, turn.StateVersion, Now);
        await harness.DbContext.SaveChangesAsync();

        // Act
        var applied = await harness.Apply.HandleAsync(turn.Id);

        // Assert
        applied.IsFailure.Should().BeTrue();
        applied.Error!.Code.Should().Be("INVALID_TRANSITION");

        var storedTurn = await harness.DbContext.Turns.AsNoTracking()
            .SingleAsync(item => item.Id == turn.Id);
        storedTurn.State.Should().Be(TurnState.Expired);

        var command = await harness.DbContext.DomainCommands.AsNoTracking().SingleAsync();
        command.Status.Should().Be(DomainCommandStatus.Failed);
        command.Attempts.Should().Be(1);

        (await harness.DbContext.Events.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ApplyTurnPlan_schedules_tts_but_does_not_run_it()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        harness.Nlu.Respond = _ => CreateTaskResponse("купить хлеб");

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "купи хлеб"));
        await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Act
        var applied = await harness.Apply.HandleAsync(submitted.Value.TurnId);

        // Assert
        applied.IsSuccess.Should().BeTrue();

        harness.Scheduler.Scheduled.Should().ContainSingle(job => job.Kind == JobKind.Tts)
            .Which.TargetId.Should().Be(applied.Value.AnswerId!.Value);

        harness.Tts.Requests.Should().BeEmpty();

        var answer = await harness.DbContext.Answers.AsNoTracking().SingleAsync();
        answer.AudioStatus.Should().Be(AnswerAudioStatus.Pending);
        answer.AudioObjectKey.Should().BeNull();
    }

    [Fact]
    public async Task SynthesizeAnswer_marks_audio_ready_and_keeps_turn_completed()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        harness.Nlu.Respond = _ => CreateTaskResponse("купить хлеб");

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "купи хлеб"));
        await harness.Interpret.HandleAsync(submitted.Value.TurnId);
        var applied = await harness.Apply.HandleAsync(submitted.Value.TurnId);

        harness.Tts.Respond = request => new TtsResult(
            request.AnswerId,
            "tts/0123456789abcdef.opus",
            "audio/ogg",
            "ogg_opus",
            DurationMs: 4200,
            Cached: false);

        // Act
        var synthesized = await harness.Synthesize.HandleAsync(applied.Value.AnswerId!.Value);

        // Assert
        synthesized.IsSuccess.Should().BeTrue();
        synthesized.Value.AudioStatus.Should().Be(AnswerAudioStatus.Ready);

        var answer = await harness.DbContext.Answers.AsNoTracking().SingleAsync();
        answer.AudioStatus.Should().Be(AnswerAudioStatus.Ready);
        answer.AudioObjectKey.Should().Be("tts/0123456789abcdef.opus");
        answer.AudioDurationMs.Should().Be(4200);

        var turn = await harness.DbContext.Turns.AsNoTracking()
            .SingleAsync(item => item.Id == submitted.Value.TurnId);
        turn.State.Should().Be(TurnState.Completed);
        turn.ErrorCode.Should().BeNull();

        harness.Tts.Requests.Should().ContainSingle()
            .Which.Text.Should().Be(answer.Text);
    }

    [Fact]
    public async Task SynthesizeAnswer_accepts_zero_duration_from_contract()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        var answer = await CreateAppliedAnswerAsync(harness);

        harness.Tts.Respond = request => new TtsResult(
            request.AnswerId,
            "tts/0123456789abcdef.opus",
            "audio/ogg",
            "ogg_opus",
            DurationMs: 0,
            Cached: true);

        // Act
        var synthesized = await harness.Synthesize.HandleAsync(answer.Value.AnswerId!.Value);

        // Assert
        synthesized.IsSuccess.Should().BeTrue();
        synthesized.Value.AudioStatus.Should().Be(AnswerAudioStatus.Ready);

        var stored = await harness.DbContext.Answers.AsNoTracking().SingleAsync();
        stored.AudioStatus.Should().Be(AnswerAudioStatus.Ready);
        stored.AudioDurationMs.Should().Be(0);
    }

    [Fact]
    public async Task SynthesizeAnswer_transient_failure_keeps_turn_completed_and_audio_pending()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        var answer = await CreateAppliedAnswerAsync(harness);

        harness.Tts.Respond = _ => throw new TtsException("tts is unavailable", 503);

        // Act
        Func<Task> act = () => harness.Synthesize.HandleAsync(answer.Value.AnswerId!.Value);

        // Assert
        await act.Should().ThrowAsync<TtsException>();

        var stored = await harness.DbContext.Answers.AsNoTracking().SingleAsync();
        stored.AudioStatus.Should().Be(AnswerAudioStatus.Pending);
        stored.AudioObjectKey.Should().BeNull();

        var turn = await harness.DbContext.Turns.AsNoTracking()
            .SingleAsync(item => item.Id == answer.Value.TurnId);
        turn.State.Should().Be(TurnState.Completed);
        turn.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task SynthesizeAnswer_permanent_failure_marks_audio_failed()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        var answer = await CreateAppliedAnswerAsync(harness);

        harness.Tts.Respond = _ => throw new TtsException("bad request", 400);

        // Act
        var synthesized = await harness.Synthesize.HandleAsync(answer.Value.AnswerId!.Value);

        // Assert
        synthesized.IsFailure.Should().BeTrue();
        synthesized.Error!.Code.Should().Be("ANSWER_AUDIO_SYNTHESIS_FAILED");

        var stored = await harness.DbContext.Answers.AsNoTracking().SingleAsync();
        stored.AudioStatus.Should().Be(AnswerAudioStatus.Failed);

        var turn = await harness.DbContext.Turns.AsNoTracking()
            .SingleAsync(item => item.Id == answer.Value.TurnId);
        turn.State.Should().Be(TurnState.Completed);
        turn.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task SynthesizeAnswer_skips_answer_that_is_not_pending()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        var answer = await CreateAppliedAnswerAsync(harness);

        harness.Tts.Respond = request => new TtsResult(
            request.AnswerId,
            "tts/0123456789abcdef.opus",
            "audio/ogg",
            "ogg_opus",
            DurationMs: 1000,
            Cached: false);

        await harness.Synthesize.HandleAsync(answer.Value.AnswerId!.Value);

        // Act
        var repeated = await harness.Synthesize.HandleAsync(answer.Value.AnswerId!.Value);

        // Assert
        repeated.IsSuccess.Should().BeTrue();
        repeated.Value.AudioStatus.Should().Be(AnswerAudioStatus.Ready);
        harness.Tts.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task SynthesizeAnswer_returns_not_found_for_unknown_answer()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();

        // Act
        var synthesized = await harness.Synthesize.HandleAsync(Guid.NewGuid());

        // Assert
        synthesized.IsFailure.Should().BeTrue();
        synthesized.Error!.Code.Should().Be("ANSWER_NOT_FOUND");
    }

    private async Task<Result<ApplyTurnPlanResult>> CreateAppliedAnswerAsync(
        TurnsIntakeHarness harness)
    {
        harness.Nlu.Respond = _ => CreateTaskResponse("купить хлеб");

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "купи хлеб"));
        await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        return await harness.Apply.HandleAsync(submitted.Value.TurnId);
    }

    private async Task<Turn> CreateApplyingTurnAsync(TurnsIntakeHarness harness)
    {
        Turn turn = Turn.Create(
            Guid.NewGuid(),
            TurnState.Interpreting,
            Guid.NewGuid(),
            Now.AddMinutes(5),
            Now,
            text: "создай встречу").Value;

        turn.ApplyPlan(            TurnTransition.WritePlan,
            "{}",
            turn.StateVersion,
            Now);

        // Встреча без даты и времени не проходит валидацию Events.Domain.
        var payload = new EventCreatePayload(
            Kind: "meeting",
            Description: "планёрка",
            SourceText: "создай встречу",
            StartsOn: null,
            StartsAt: null,
            Duration: null,
            IsDeadline: false);

        var createdCommand = DomainCommand.Create(
            turn.Id,
            seq: 0,
            DomainCommandOperation.EventCreate,
            EventCreatePayloadJson.Serialize(payload),
            Now).Value;

        await harness.DbContext.Turns.AddAsync(turn);
        await harness.DbContext.DomainCommands.AddAsync(createdCommand);
        await harness.DbContext.SaveChangesAsync();

        return turn;
    }

    private Task<TurnsIntakeHarness> CreateHarnessAsync() =>
        TurnsIntakeHarness.CreateAsync(_postgres.GetConnectionString(), Now);

    private static NluResponse CreateTaskResponse(string title) => new(
        Guid.Empty,
        "create_task",
        0.9,
        new NluEntities(Description: title),
        Embedding: Embedding());

    private static NluResponse CreateMeetingResponse(string title, DateOnly day, TimeOnly time) => new(
        Guid.Empty,
        "create_meeting",
        0.9,
        new NluEntities(
            Description: title,
            StartsOn: day.ToString("yyyy-MM-dd"),
            StartsAt: time.ToString("HH:mm")),
        Embedding: Embedding());

    private static NluResponse ListPeriodResponse(
        string from,
        string to,
        string? typeFilter = null) => new(
        Guid.Empty,
        "list_period",
        0.9,
        new NluEntities(
            PeriodFrom: from,
            PeriodTo: to,
            TypeFilter: typeFilter));

    private static NluEmbedding Embedding() =>
        new("fake-embedding", 3, [0.1, 0.2, 0.3]);
}
