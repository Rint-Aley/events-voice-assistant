using Microsoft.EntityFrameworkCore;
using TaskManager.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace TaskManager.IntegrationTests.Persistence;

public sealed class MigrationsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:0.8.7-pg17")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Migrations_apply_to_postgres_and_create_expected_schema()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TaskManagerDbContext>()
            .UseTaskManagerPostgres(_postgres.GetConnectionString())
            .Options;

        await using var dbContext = new TaskManagerDbContext(options);

        // Act
        await dbContext.Database.MigrateAsync();

        // Assert
        var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync();
        appliedMigrations.Should().Contain("20261030141449_InitialPersistence");
        appliedMigrations.Should().BeEquivalentTo(dbContext.Database.GetMigrations());

        var tableNames = await ReadStringsAsync(
            dbContext,
            """
            select table_name as "Value"
            from information_schema.tables
            where table_schema = 'public' and table_type = 'BASE TABLE'
            """);

        tableNames.Should().Contain(
        [
            "turns",
            "drafts",
            "answers",
            "answer_events",
            "domain_commands",
            "events",
        ]);

        var turnColumns = await ReadColumnsAsync(dbContext, "turns");
        turnColumns.Should().Contain("state_version:integer:NO");

        var eventColumns = await ReadColumnsAsync(dbContext, "events");
        eventColumns.Should().Contain(
        [
            "description:text:NO",
            "source_text:text:YES",
            "starts_on:date:YES",
            "starts_at:time without time zone:YES",
            "duration:interval:YES",
            "is_deadline:boolean:NO",
            "embedding:vector:YES",
            "embedding_model:text:YES",
        ]);

        var draftColumns = await ReadColumnsAsync(dbContext, "drafts");
        draftColumns.Should().Contain(
        [
            "source_text:text:YES",
            "clarification_round:integer:NO",
            "is_deadline:boolean:NO",
            "embedding:vector:YES",
        ]);

        var answerColumns = await ReadColumnsAsync(dbContext, "answers");
        answerColumns.Should().Contain(
        [
            "total_count:integer:YES",
            "warnings:jsonb:NO",
        ]);

        var commandColumns = await ReadColumnsAsync(dbContext, "domain_commands");
        commandColumns.Should().Contain("payload:jsonb:NO");

        var embeddingTypes = await ReadStringsAsync(
            dbContext,
            """
            select format_type(attribute.atttypid, attribute.atttypmod) as "Value"
            from pg_attribute attribute
            join pg_class table_info on table_info.oid = attribute.attrelid
            where table_info.relname in ('events', 'drafts')
              and attribute.attname = 'embedding'
              and not attribute.attisdropped
            """);

        embeddingTypes.Should().HaveCount(2).And.OnlyContain(type => type == "vector(1536)");

        var eventIndexes = await ReadIndexDefinitionsAsync(dbContext, "events");
        eventIndexes.Should().Contain(
            definition => definition.Contains("events_embedding_hnsw", StringComparison.Ordinal)
                && definition.Contains("hnsw", StringComparison.OrdinalIgnoreCase)
                && definition.Contains("vector_cosine_ops", StringComparison.Ordinal));

        var turnIndexes = await ReadIndexDefinitionsAsync(dbContext, "turns");
        turnIndexes.Should().Contain(
            definition => definition.Contains("turns_one_active", StringComparison.Ordinal)
                && definition.Contains("WHERE", StringComparison.Ordinal));
        turnIndexes.Should().Contain(
            definition => definition.Contains("turns_recent", StringComparison.Ordinal)
                && definition.Contains("DESC", StringComparison.Ordinal));
    }

    private static Task<List<string>> ReadColumnsAsync(
        TaskManagerDbContext dbContext,
        string tableName) =>
        ReadStringsAsync(
            dbContext,
            $"""
            select column_name || ':' ||
                   case when data_type = 'USER-DEFINED' then udt_name else data_type end || ':' ||
                   is_nullable as "Value"
            from information_schema.columns
            where table_schema = 'public' and table_name = '{tableName}'
            """);

    private static Task<List<string>> ReadIndexDefinitionsAsync(
        TaskManagerDbContext dbContext,
        string tableName) =>
        ReadStringsAsync(
            dbContext,
            $"""
            select indexdef as "Value"
            from pg_indexes
            where schemaname = 'public' and tablename = '{tableName}'
            """);

    private static Task<List<string>> ReadStringsAsync(
        TaskManagerDbContext dbContext,
        string sql) =>
        dbContext.Database.SqlQueryRaw<string>(sql).ToListAsync();
}
