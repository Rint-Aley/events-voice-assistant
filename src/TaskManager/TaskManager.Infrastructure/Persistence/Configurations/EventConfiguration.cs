using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgvector;
using TaskManager.Events.Domain;
using TaskManager.Turns.Domain;

namespace TaskManager.Infrastructure.Persistence.Configurations;

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    // В MVP пользователь один; значение приходит из USER_ID, когда появится конфигурация.
    private const string DefaultUserId = "demo-user";

    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("events", table =>
        {
            table.HasCheckConstraint(
                "events_kind_check",
                "kind in ('task', 'reminder', 'meeting')");
            table.HasCheckConstraint(
                "events_status_check",
                "status in ('active', 'done', 'cancelled')");
            table.HasCheckConstraint(
                "events_description_not_blank",
                "length(btrim(description)) > 0");
            table.HasCheckConstraint(
                "events_model_pair",
                "(embedding is null) = (embedding_model is null)");
            table.HasCheckConstraint(
                "events_time_requires_date",
                "starts_at is null or starts_on is not null");
            table.HasCheckConstraint(
                "events_duration_requires_time",
                "duration is null or starts_at is not null");
            table.HasCheckConstraint(
                "events_duration_positive",
                "duration is null or duration > interval '0'");
            table.HasCheckConstraint(
                "events_duration_only_meeting",
                "duration is null or kind = 'meeting'");
            table.HasCheckConstraint(
                "events_deadline_only_task",
                "not is_deadline or kind = 'task'");
        });

        builder.HasKey(@event => @event.Id);

        builder.Property(@event => @event.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property<string>("UserId")
            .HasColumnName("user_id")
            .HasDefaultValue(DefaultUserId)
            .IsRequired();

        builder.Property(@event => @event.Kind)
            .HasColumnName("kind")
            .HasConversion(
                kind => EnumText.ToDb(kind),
                value => EnumText.EventKindFromDb(value))
            .IsRequired();

        builder.Property(@event => @event.Status)
            .HasColumnName("status")
            .HasConversion(
                status => EnumText.ToDb(status),
                value => EnumText.EventStatusFromDb(value))
            .HasDefaultValue(EventStatus.Active)
            .IsRequired();

        builder.Property(@event => @event.Description)
            .HasColumnName("description")
            .IsRequired();

        builder.Property(@event => @event.SourceText)
            .HasColumnName("source_text");

        // Модель и размерность зафиксированы: text-embedding-3-small, 1536.
        builder.Property<Vector?>("Embedding")
            .HasColumnName("embedding")
            .HasColumnType($"vector({EventEmbedding.Dimensions})");

        builder.Property<string?>("EmbeddingModel")
            .HasColumnName("embedding_model");

        builder.Property(@event => @event.StartsOn)
            .HasColumnName("starts_on")
            .HasColumnType("date");

        builder.Property(@event => @event.StartsAt)
            .HasColumnName("starts_at")
            .HasColumnType("time without time zone");

        builder.Property(@event => @event.Duration)
            .HasColumnName("duration")
            .HasColumnType("interval");

        builder.Property(@event => @event.IsDeadline)
            .HasColumnName("is_deadline")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property<DateTimeOffset>("CreatedAt")
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property<DateTimeOffset>("UpdatedAt")
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property<Guid?>("SourceTurnId")
            .HasColumnName("source_turn_id");

        builder.HasOne<Turn>()
            .WithMany()
            .HasForeignKey("SourceTurnId")
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(@event => new { @event.StartsOn, @event.StartsAt })
            .HasDatabaseName("events_by_date");

        builder.HasIndex(@event => new { @event.Status, @event.StartsOn })
            .HasDatabaseName("events_by_status");

        builder.HasIndex("SourceTurnId")
            .HasDatabaseName("events_by_source_turn");

        // Поиск идёт по косинусной близости, поэтому индекс — HNSW с vector_cosine_ops.
        builder.HasIndex("Embedding")
            .HasDatabaseName("events_embedding_hnsw")
            .HasMethod("hnsw")
            .HasOperators("vector_cosine_ops");

        builder.Ignore(@event => @event.IsVisible);
        builder.Ignore(@event => @event.Start);
        builder.Ignore(@event => @event.End);
    }
}
