using Microsoft.EntityFrameworkCore;
using TaskManager.Application.Abstractions;
using TaskManager.Events.Domain;
using TaskManager.Turns.Application.Clients.Nlu;
using TaskManager.Turns.Application.Clients.Stt;
using TaskManager.Turns.Application.Scenarios;
using TaskManager.Turns.Domain;
using Testcontainers.PostgreSql;

namespace TaskManager.IntegrationTests.Turns;

public sealed class TurnsIntakeTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("pgvector/pgvector:0.8.7-pg17").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task SubmitText_creates_interpreting_turn_and_reuses_it_by_idempotency_key()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        Guid sessionId = Guid.NewGuid();
        Guid idempotencyKey = Guid.NewGuid();

        // Act
        var first = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(sessionId, idempotencyKey, "создай задачу"));
        var repeat = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(sessionId, idempotencyKey, "другой текст"));

        // Assert
        first.IsSuccess.Should().BeTrue();
        first.Value.State.Should().Be(TurnState.Interpreting);

        repeat.IsSuccess.Should().BeTrue();
        repeat.Value.TurnId.Should().Be(first.Value.TurnId);
        repeat.Value.State.Should().Be(TurnState.Interpreting);

        var turns = await harness.DbContext.Turns.AsNoTracking().ToListAsync();
        turns.Should().ContainSingle();
        turns[0].Text.Should().Be("создай задачу");

        harness.Scheduler.Scheduled.Should().ContainSingle()
            .Which.Should().Be((JobKind.Nlu, first.Value.TurnId));
    }

    [Fact]
    public async Task SubmitVoice_schedules_stt_and_handle_result_moves_turn_to_interpreting()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        Guid sessionId = Guid.NewGuid();

        // Act
        var submitted = await harness.SubmitVoice.HandleAsync(
            new SubmitVoiceCommand(sessionId, Guid.NewGuid(), "audio/demo/voice.webm"));
        var handled = await harness.HandleSttResult.HandleAsync(new SttResult(
            submitted.Value.TurnId,
            "напомни завтра в 10 позвонить Ивану",
            "ru",
            0.92m,
            Empty: false,
            DurationMs: 1500,
            Provider: "fake"));

        // Assert
        submitted.IsSuccess.Should().BeTrue();
        submitted.Value.State.Should().Be(TurnState.Transcribing);
        harness.Scheduler.Scheduled.Should().Contain(
            (JobKind.Stt, submitted.Value.TurnId));

        handled.IsSuccess.Should().BeTrue();
        handled.Value.State.Should().Be(TurnState.Interpreting);

        var turn = await harness.DbContext.Turns.AsNoTracking()
            .SingleAsync(item => item.Id == submitted.Value.TurnId);
        turn.Text.Should().Be("напомни завтра в 10 позвонить Ивану");
        turn.SttConfidence.Should().Be(0.92m);

        harness.Scheduler.Scheduled.Should().Contain(
            (JobKind.Nlu, submitted.Value.TurnId));
    }

    [Fact]
    public async Task InterpretTurn_parses_create_task_without_optional_old_nlu_fields()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        harness.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "create_task",
            0.9,
            new NluEntities(Description: "купить хлеб"),
            Embedding: Embedding());

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "купи хлеб"));

        // Act
        var interpreted = await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Assert
        interpreted.IsSuccess.Should().BeTrue();
        interpreted.Value.Intent.Should().Be(TurnIntent.CreateTask);
        interpreted.Value.State.Should().Be(TurnState.Applying);

        var turn = await harness.DbContext.Turns.AsNoTracking()
            .SingleAsync(item => item.Id == submitted.Value.TurnId);
        turn.State.Should().Be(TurnState.Applying);
        turn.Plan.Should().NotBeNullOrWhiteSpace();
        (await harness.DbContext.Events.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task InterpretTurn_keeps_draft_and_asks_again_for_two_rounds_then_completes()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        harness.Nlu.Respond = _ => ClarifyResponse("поездка");
        Guid sessionId = Guid.NewGuid();

        // Act: первый раунд
        var first = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(sessionId, Guid.NewGuid(), "создай встречу"));
        var firstInterpreted = await harness.Interpret.HandleAsync(first.Value.TurnId);

        // Assert: первый вопрос и черновик
        firstInterpreted.Value.State.Should().Be(TurnState.NeedsClarification);
        var firstDraft = await harness.DbContext.Drafts.AsNoTracking().SingleAsync();
        firstDraft.TurnId.Should().Be(first.Value.TurnId);
        firstDraft.ClarificationRound.Should().Be(1);
        firstDraft.Description.Should().Be("поездка");

        var snapshot = await harness.Snapshot.HandleAsync(first.Value.TurnId);
        snapshot.IsSuccess.Should().BeTrue();
        snapshot.Value.State.Should().Be(TurnState.NeedsClarification);
        snapshot.Value.Answer.Should().NotBeNull();
        snapshot.Value.Answer!.Text.Should().Be("На какой день поставить «поездка»?");
        snapshot.Value.Answer.Events.Should().BeEmpty();
        snapshot.Value.Answer.TotalCount.Should().BeNull();
        snapshot.Value.Answer.Warnings.Should().BeEmpty();
        snapshot.Value.Answer.AudioStatus.Should().Be(AnswerAudioStatus.Pending);

        // Act: второй раунд
        var second = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(sessionId, Guid.NewGuid(), "в пятницу"));
        var secondInterpreted = await harness.Interpret.HandleAsync(second.Value.TurnId);

        // Assert: второй вопрос, черновик заменён
        secondInterpreted.Value.State.Should().Be(TurnState.NeedsClarification);
        var secondDraft = await harness.DbContext.Drafts.AsNoTracking().SingleAsync();
        secondDraft.TurnId.Should().Be(second.Value.TurnId);
        secondDraft.ClarificationRound.Should().Be(2);

        // Act: третий ответ после двух раундов
        var third = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(sessionId, Guid.NewGuid(), "всё равно не понимаю"));
        var thirdInterpreted = await harness.Interpret.HandleAsync(third.Value.TurnId);

        // Assert: ход завершён, черновик удалён
        thirdInterpreted.Value.State.Should().Be(TurnState.Completed);
        (await harness.DbContext.Drafts.AsNoTracking().CountAsync()).Should().Be(0);

        var finalSnapshot = await harness.Snapshot.HandleAsync(third.Value.TurnId);
        finalSnapshot.Value.Answer!.Text.Should().Be(
            "Не удалось уточнить. Сформулируй запрос иначе.");
    }

    [Fact]
    public async Task InterpretTurn_drops_invalid_date_and_time_slots_and_asks_general_questions()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        harness.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "create_meeting",
            0.9,
            new NluEntities(
                Description: "созвон",
                StartsOn: "2026-02-30",
                StartsAt: "25:61"),
            Embedding: Embedding());

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "создай встречу 30 февраля"));

        // Act
        var interpreted = await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Assert
        interpreted.Value.State.Should().Be(TurnState.NeedsClarification);

        var draft = await harness.DbContext.Drafts.AsNoTracking().SingleAsync();
        draft.StartsOn.Should().BeNull();
        draft.StartsAt.Should().BeNull();
        draft.Description.Should().Be("созвон");

        var snapshot = await harness.Snapshot.HandleAsync(submitted.Value.TurnId);
        snapshot.Value.Answer!.Text.Should().Be(
            "Такой даты не существует. На какой день поставить?");
    }

    [Fact]
    public async Task InterpretTurn_drops_invalid_time_but_keeps_valid_date()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        var day = new DateOnly(2026, 11, 2);
        harness.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "create_reminder",
            0.9,
            new NluEntities(
                Description: "позвонить Ивану",
                StartsOn: "2026-11-02",
                StartsAt: "25:61"),
            Embedding: Embedding());

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "напомни 2 ноября"));

        // Act
        var interpreted = await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Assert
        interpreted.Value.State.Should().Be(TurnState.NeedsClarification);

        var draft = await harness.DbContext.Drafts.AsNoTracking().SingleAsync();
        draft.StartsOn.Should().Be(day);
        draft.StartsAt.Should().BeNull();

        var snapshot = await harness.Snapshot.HandleAsync(submitted.Value.TurnId);
        snapshot.Value.Answer!.Text.Should().Be(
            "Такого времени не существует. На какое время поставить?");
    }

    [Fact]
    public async Task InterpretTurn_keeps_draft_and_asks_to_move_past_time()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        var day = new DateOnly(2026, 10, 30);
        var time = new TimeOnly(10, 0);
        harness.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "create_reminder",
            0.9,
            new NluEntities(
                Description: "позвонить Ивану",
                StartsOn: "2026-10-30",
                StartsAt: "10:00"),
            Embedding: Embedding());

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "напомни сегодня в 10"));

        // Act
        var interpreted = await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Assert
        interpreted.Value.State.Should().Be(TurnState.NeedsClarification);

        var draft = await harness.DbContext.Drafts.AsNoTracking().SingleAsync();
        draft.StartsOn.Should().Be(day);
        draft.StartsAt.Should().Be(time);

        var snapshot = await harness.Snapshot.HandleAsync(submitted.Value.TurnId);
        snapshot.Value.Answer!.Text.Should().Be(
            "Это время уже прошло. На какое время перенести?");
    }

    [Fact]
    public async Task InterpretTurn_fails_with_nlu_failed_when_description_has_no_embedding()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        Guid sessionId = Guid.NewGuid();
        harness.Nlu.Respond = _ => ClarifyResponse("поездка");

        var withDraft = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(sessionId, Guid.NewGuid(), "создай встречу"));
        await harness.Interpret.HandleAsync(withDraft.Value.TurnId);
        var draft = await harness.DbContext.Drafts.AsNoTracking().SingleAsync();

        harness.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "create_task",
            0.9,
            new NluEntities(Description: "купить хлеб"),
            Embedding: null);

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(sessionId, Guid.NewGuid(), "купи хлеб"));

        // Act
        var interpreted = await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Assert
        interpreted.IsSuccess.Should().BeTrue();
        interpreted.Value.State.Should().Be(TurnState.Failed);

        var turn = await harness.DbContext.Turns.AsNoTracking()
            .SingleAsync(item => item.Id == submitted.Value.TurnId);
        turn.State.Should().Be(TurnState.Failed);
        turn.ErrorCode.Should().Be("NLU_FAILED");
        (await harness.DbContext.Events.AsNoTracking().CountAsync()).Should().Be(0);

        var unchanged = await harness.DbContext.Drafts.AsNoTracking().SingleAsync();
        unchanged.Id.Should().Be(draft.Id);
        unchanged.ClarificationRound.Should().Be(1);
    }

    [Fact]
    public async Task InterpretTurn_fails_with_nlu_failed_when_search_has_no_embedding()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        harness.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "search",
            0.9,
            new NluEntities(Query: "поездка"),
            Embedding: null);

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "найди поездку"));

        // Act
        var interpreted = await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Assert
        // nlu.response требует embedding для search: план без вектора — нарушение контракта.
        interpreted.IsSuccess.Should().BeTrue();
        interpreted.Value.State.Should().Be(TurnState.Failed);

        var turn = await harness.DbContext.Turns.AsNoTracking()
            .SingleAsync(item => item.Id == submitted.Value.TurnId);
        turn.ErrorCode.Should().Be("NLU_FAILED");
    }

    [Fact]
    public async Task InterpretTurn_resets_previous_draft_on_intent_other_than_create_or_clarify()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        Guid sessionId = Guid.NewGuid();
        harness.Nlu.Respond = _ => ClarifyResponse("поездка");

        var withDraft = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(sessionId, Guid.NewGuid(), "создай встречу"));
        await harness.Interpret.HandleAsync(withDraft.Value.TurnId);
        (await harness.DbContext.Drafts.AsNoTracking().CountAsync()).Should().Be(1);

        harness.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "list_period",
            0.9,
            new NluEntities(PeriodFrom: "2026-10-30", PeriodTo: "2026-11-05"));

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(sessionId, Guid.NewGuid(), "что у меня на неделе"));

        // Act
        var interpreted = await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Assert
        interpreted.Value.State.Should().Be(TurnState.Completed);
        (await harness.DbContext.Drafts.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CancelDraft_is_idempotent_and_returns_confirmation_text()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        harness.Nlu.Respond = _ => ClarifyResponse("поездка");

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "создай встречу"));
        await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Act
        var first = await harness.CancelDraft.HandleAsync(submitted.Value.TurnId);
        var second = await harness.CancelDraft.HandleAsync(submitted.Value.TurnId);
        var unknown = await harness.CancelDraft.HandleAsync(Guid.NewGuid());

        // Assert
        first.IsSuccess.Should().BeTrue();
        first.Value.Text.Should().Be("Хорошо, не сохраняю");
        second.IsSuccess.Should().BeTrue();
        second.Value.Text.Should().Be("Хорошо, не сохраняю");

        (await harness.DbContext.Drafts.AsNoTracking().CountAsync()).Should().Be(0);

        var turn = await harness.DbContext.Turns.AsNoTracking()
            .SingleAsync(item => item.Id == submitted.Value.TurnId);
        turn.State.Should().Be(TurnState.NeedsClarification);

        unknown.IsFailure.Should().BeTrue();
        unknown.Error!.Code.Should().Be("TURN_NOT_FOUND");
    }

    [Fact]
    public async Task Second_active_turn_is_rejected_but_same_idempotency_key_returns_existing_turn()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        Guid sessionId = Guid.NewGuid();
        Guid idempotencyKey = Guid.NewGuid();

        var first = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(sessionId, idempotencyKey, "создай задачу"));

        // Act
        var busy = await harness.SubmitVoice.HandleAsync(
            new SubmitVoiceCommand(sessionId, Guid.NewGuid(), "audio/demo/voice.webm"));
        var repeated = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(sessionId, idempotencyKey, "создай задачу"));

        // Assert
        busy.IsFailure.Should().BeTrue();
        busy.Error!.Code.Should().Be("SESSION_BUSY");

        repeated.IsSuccess.Should().BeTrue();
        repeated.Value.TurnId.Should().Be(first.Value.TurnId);

        (await harness.DbContext.Turns.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task InterpretTurn_removes_previous_draft_after_successful_write()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        Guid sessionId = Guid.NewGuid();
        harness.Nlu.Respond = _ => ClarifyResponse("поездка");

        var clarifying = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(sessionId, Guid.NewGuid(), "создай встречу"));
        await harness.Interpret.HandleAsync(clarifying.Value.TurnId);
        (await harness.DbContext.Drafts.AsNoTracking().CountAsync()).Should().Be(1);

        harness.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "create_meeting",
            0.9,
            new NluEntities(
                Description: "поездка",
                StartsOn: "2026-11-02",
                StartsAt: "15:00"),
            Embedding: Embedding());

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(sessionId, Guid.NewGuid(), "в понедельник в три"));

        // Act
        var interpreted = await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Assert
        interpreted.Value.State.Should().Be(TurnState.Applying);
        (await harness.DbContext.Drafts.AsNoTracking().CountAsync()).Should().Be(0);
        (await harness.DbContext.Turns.AsNoTracking()
            .CountAsync(item => item.Draft != null)).Should().Be(0);

        // Применение команд завершает ход; сессия освобождается.
        await harness.DbContext.Database.ExecuteSqlInterpolatedAsync(
            $"update turns set state = 'COMPLETED' where id = {submitted.Value.TurnId}");

        // Новый запрос не наследует чужие слоты и номер раунда.
        harness.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "create_task",
            0.9,
            new NluEntities(Description: "купить хлеб"),
            Embedding: Embedding());

        var next = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(sessionId, Guid.NewGuid(), "купи хлеб"));
        await harness.Interpret.HandleAsync(next.Value.TurnId);

        harness.Nlu.Requests.Should().HaveCount(3);
        harness.Nlu.Requests[2].Draft.Should().BeNull();
    }

    [Fact]
    public async Task InterpretTurn_passes_previous_draft_to_nlu()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        Guid sessionId = Guid.NewGuid();
        harness.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "create_meeting",
            0.9,
            new NluEntities(Description: "поездка"),
            Embedding: Embedding());

        var first = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(sessionId, Guid.NewGuid(), "создай встречу"));
        await harness.Interpret.HandleAsync(first.Value.TurnId);

        harness.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "create_meeting",
            0.9,
            new NluEntities(Description: "поездка", StartsOn: "2026-11-02"),
            Embedding: Embedding());

        var second = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(sessionId, Guid.NewGuid(), "в понедельник"));

        // Act
        var interpreted = await harness.Interpret.HandleAsync(second.Value.TurnId);

        // Assert
        interpreted.Value.State.Should().Be(TurnState.NeedsClarification);

        harness.Nlu.Requests.Should().HaveCount(2);
        NluDraft? draft = harness.Nlu.Requests[1].Draft;
        draft.Should().NotBeNull();
        draft!.Kind.Should().Be(DraftKind.Meeting);
        draft.Description.Should().Be("поездка");
        draft.StartsOn.Should().BeNull();

        var stored = await harness.DbContext.Drafts.AsNoTracking().SingleAsync();
        stored.TurnId.Should().Be(second.Value.TurnId);
        stored.ClarificationRound.Should().Be(2);
    }

    [Fact]
    public async Task InterpretTurn_does_not_send_empty_draft_to_nlu()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        Guid sessionId = Guid.NewGuid();
        harness.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "clarify",
            0.8,
            Entities: null,
            Embedding: null);

        var first = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(sessionId, Guid.NewGuid(), "не понял"));
        await harness.Interpret.HandleAsync(first.Value.TurnId);

        (await harness.DbContext.Drafts.AsNoTracking().CountAsync()).Should().Be(1);

        var second = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(sessionId, Guid.NewGuid(), "ещё раз"));

        // Act
        var interpreted = await harness.Interpret.HandleAsync(second.Value.TurnId);

        // Assert
        interpreted.Value.State.Should().Be(TurnState.NeedsClarification);

        harness.Nlu.Requests.Should().HaveCount(2);
        harness.Nlu.Requests[1].Draft.Should().BeNull();
    }

    [Fact]
    public async Task InterpretTurn_sends_local_moment_and_time_zone_to_nlu()
    {
        // Arrange
        TimeZoneInfo moscow = TimeZoneInfo.CreateCustomTimeZone(
            "Europe/Moscow",
            TimeSpan.FromHours(3),
            "Europe/Moscow",
            "Europe/Moscow");
        var now = new DateTimeOffset(2026, 10, 30, 21, 30, 0, TimeSpan.Zero);
        await using TurnsIntakeHarness harness = await TurnsIntakeHarness.CreateAsync(
            _postgres.GetConnectionString(),
            now,
            moscow);

        harness.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "create_task",
            0.9,
            new NluEntities(Description: "купить хлеб"),
            Embedding: Embedding());

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "купи хлеб"));

        // Act
        await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Assert
        NluRequest request = harness.Nlu.Requests.Single();
        request.TimeZone.Should().Be("Europe/Moscow");
        request.Now.Offset.Should().Be(TimeSpan.FromHours(3));
        request.Now.UtcDateTime.Should().Be(now.UtcDateTime);
    }

    [Fact]
    public async Task HandleSttResult_with_empty_result_fails_turn_with_empty_transcript()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        var submitted = await harness.SubmitVoice.HandleAsync(
            new SubmitVoiceCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "audio/demo/silence.webm"));

        // Act
        var handled = await harness.HandleSttResult.HandleAsync(new SttResult(
            submitted.Value.TurnId,
            Text: null,
            Language: "ru",
            Confidence: 0m,
            Empty: true,
            DurationMs: 900,
            Provider: "fake"));

        // Assert
        handled.IsSuccess.Should().BeTrue();
        handled.Value.State.Should().Be(TurnState.Failed);

        var turn = await harness.DbContext.Turns.AsNoTracking()
            .SingleAsync(item => item.Id == submitted.Value.TurnId);
        turn.ErrorCode.Should().Be("EMPTY_TRANSCRIPT");
        harness.Scheduler.Scheduled.Should()
            .NotContain(job => job.Kind == JobKind.Nlu);
    }

    [Fact]
    public async Task InterpretTurn_rejects_repeated_result_after_turn_is_completed()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        harness.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "list_period",
            0.9,
            new NluEntities());

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "что у меня на неделе"));
        await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Act
        var repeated = await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Assert
        repeated.IsFailure.Should().BeTrue();
        repeated.Error!.Code.Should().Be("INVALID_TRANSITION");
        harness.Nlu.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task InterpretTurn_accepts_task_without_date_and_time()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        harness.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "create_task",
            0.9,
            new NluEntities(Description: "купить хлеб"),
            Embedding: Embedding());

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "купи хлеб"));

        // Act
        var interpreted = await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Assert
        interpreted.Value.State.Should().Be(TurnState.Applying);
        (await harness.DbContext.Drafts.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task InterpretTurn_asks_for_date_when_only_time_is_given()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        harness.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "create_task",
            0.9,
            new NluEntities(Description: "позвонить Ивану", StartsAt: "15:00"),
            Embedding: Embedding());

        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "позвонить Ивану в 15:00"));

        // Act
        var interpreted = await harness.Interpret.HandleAsync(submitted.Value.TurnId);

        // Assert
        interpreted.Value.State.Should().Be(TurnState.NeedsClarification);

        var draft = await harness.DbContext.Drafts.AsNoTracking().SingleAsync();
        draft.StartsOn.Should().BeNull();
        draft.StartsAt.Should().BeNull();

        var snapshot = await harness.Snapshot.HandleAsync(submitted.Value.TurnId);
        snapshot.Value.Answer!.Text.Should().Be(
            "На какой день поставить «позвонить Ивану»?");
    }

    private Task<TurnsIntakeHarness> CreateHarnessAsync() =>
        TurnsIntakeHarness.CreateAsync(_postgres.GetConnectionString(), Now);

    private static NluResponse ClarifyResponse(string description) => new(
        Guid.Empty,
        "clarify",
        0.8,
        new NluEntities(Description: description),
        Missing: ["date"],
        Question: $"На какой день поставить «{description}»?",
        Embedding: Embedding());

    private static NluEmbedding Embedding() =>
        new(
            EventEmbedding.Model,
            EventEmbedding.Dimensions,
            Enumerable.Repeat(0.1, EventEmbedding.Dimensions).ToArray());
}
