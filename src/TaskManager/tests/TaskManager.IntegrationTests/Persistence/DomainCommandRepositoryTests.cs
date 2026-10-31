using Microsoft.EntityFrameworkCore;
using TaskManager.Infrastructure.Persistence.Repositories;
using TaskManager.Turns.Domain;
using TaskManager.IntegrationTests.Turns;
using Testcontainers.PostgreSql;

namespace TaskManager.IntegrationTests.Persistence;

public sealed class DomainCommandRepositoryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("pgvector/pgvector:0.8.7-pg17").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ListDueTurnIds_returns_each_due_turn_once_ordered_by_earliest_command()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        var repository = new DomainCommandRepository(harness.DbContext);

        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        Guid future = Guid.NewGuid();
        Guid applied = Guid.NewGuid();

        await AddCommandsAsync(
            harness,
            Create(first, seq: 0, Now.AddSeconds(-10), DomainCommandStatus.Pending),
            Create(first, seq: 1, Now.AddMinutes(-2), DomainCommandStatus.Pending),
            Create(second, seq: 0, Now.AddMinutes(-1), DomainCommandStatus.Pending),
            Create(future, seq: 0, Now.AddMinutes(5), DomainCommandStatus.Pending),
            Create(applied, seq: 0, Now.AddMinutes(-3), DomainCommandStatus.Applied));

        // Act
        IReadOnlyList<Guid> turnIds = await repository.ListDueTurnIdsAsync(Now, limit: 50);

        // Assert
        // Postgres не принимает DISTINCT с ORDER BY вне выборки, поэтому запрос
        // схлопывает turn_id через GROUP BY и заодно отбрасывает будущие и применённые.
        turnIds.Should().Equal(first, second);
    }

    [Fact]
    public async Task ListDueTurnIds_respects_limit()
    {
        // Arrange
        await using TurnsIntakeHarness harness = await CreateHarnessAsync();
        var repository = new DomainCommandRepository(harness.DbContext);

        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        await AddCommandsAsync(
            harness,
            Create(first, seq: 0, Now.AddMinutes(-2), DomainCommandStatus.Pending),
            Create(second, seq: 0, Now.AddMinutes(-1), DomainCommandStatus.Pending));

        // Act
        IReadOnlyList<Guid> turnIds = await repository.ListDueTurnIdsAsync(Now, limit: 1);

        // Assert
        turnIds.Should().Equal(first);
    }

    private static DomainCommand Create(
        Guid turnId,
        short seq,
        DateTimeOffset nextAttemptAt,
        DomainCommandStatus status)
    {
        DateTimeOffset createdAt = Now.AddMinutes(-10);

        return DomainCommand.Restore(
            Guid.NewGuid(),
            turnId,
            seq,
            DomainCommandOperation.EventCreate,
            "{}",
            status,
            attempts: status == DomainCommandStatus.Pending ? 0 : 1,
            nextAttemptAt,
            createdAt,
            appliedAt: status == DomainCommandStatus.Applied
                ? createdAt.AddMinutes(1)
                : null).Value;
    }

    private static async Task AddCommandsAsync(
        TurnsIntakeHarness harness,
        params DomainCommand[] commands)
    {
        foreach (Guid turnId in commands.Select(command => command.TurnId).Distinct())
        {
            await harness.DbContext.Turns.AddAsync(CreateTurn(turnId));
        }

        await harness.DbContext.SaveChangesAsync();

        await harness.DbContext.DomainCommands.AddRangeAsync(commands);
        await harness.DbContext.SaveChangesAsync();
    }

    private static Turn CreateTurn(Guid turnId) =>
        Turn.Restore(
            turnId,
            Guid.NewGuid(),
            TurnState.Completed,
            Turn.InitialStateVersion + 1,
            Guid.NewGuid(),
            deadlineAt: Now.AddMinutes(5),
            createdAt: Now.AddMinutes(-10),
            updatedAt: Now.AddMinutes(-5),
            text: "задача").Value;

    private Task<TurnsIntakeHarness> CreateHarnessAsync() =>
        TurnsIntakeHarness.CreateAsync(_postgres.GetConnectionString(), Now);
}
