using TaskManager.Turns.Application.Scenarios;
using TaskManager.Turns.Domain;
using Testcontainers.PostgreSql;

namespace TaskManager.IntegrationTests.Turns;

public sealed class FailTurnTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("pgvector/pgvector:0.8.7-pg17").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task FailTurn_reports_change_only_on_first_close()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        var submitted = await harness.SubmitText.HandleAsync(
            new SubmitTextCommand(Guid.NewGuid(), Guid.NewGuid(), "создай задачу"));

        // Act
        var first = await harness.FailTurn.HandleAsync(
            submitted.Value.TurnId,
            TurnTransition.DeadlineExceeded);
        var second = await harness.FailTurn.HandleAsync(
            submitted.Value.TurnId,
            TurnTransition.DeadlineExceeded);

        // Assert
        // Повтор не должен увеличивать счётчики reconciler: ход уже закрыт.
        first.IsSuccess.Should().BeTrue();
        first.Value.Changed.Should().BeTrue();
        first.Value.State.Should().Be(TurnState.Expired);

        second.IsSuccess.Should().BeTrue();
        second.Value.Changed.Should().BeFalse();
        second.Value.State.Should().Be(TurnState.Expired);
    }

    [Fact]
    public async Task MarkAnswerAudioFailed_reports_change_only_on_first_failure()
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
        Answer answer = Answer.Create(turn.Id, "ответ", Now.AddMinutes(-1)).Value;

        await harness.DbContext.Turns.AddAsync(turn);
        await harness.DbContext.Answers.AddAsync(answer);
        await harness.DbContext.SaveChangesAsync();

        // Act
        var first = await harness.FailAudio.HandleAsync(answer.Id);
        var second = await harness.FailAudio.HandleAsync(answer.Id);

        // Assert
        first.IsSuccess.Should().BeTrue();
        first.Value.Changed.Should().BeTrue();
        first.Value.Status.Should().Be(AnswerAudioStatus.Failed);

        second.IsSuccess.Should().BeTrue();
        second.Value.Changed.Should().BeFalse();
        second.Value.Status.Should().Be(AnswerAudioStatus.Failed);
    }

    private Task<TurnsIntakeHarness> CreateHarnessAsync() =>
        TurnsIntakeHarness.CreateAsync(_postgres.GetConnectionString(), Now);
}
