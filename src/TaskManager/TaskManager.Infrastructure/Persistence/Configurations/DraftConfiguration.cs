using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgvector;
using TaskManager.Turns.Domain;

namespace TaskManager.Infrastructure.Persistence.Configurations;

internal sealed class DraftConfiguration : IEntityTypeConfiguration<Draft>
{
    public void Configure(EntityTypeBuilder<Draft> builder)
    {
        builder.ToTable("drafts", table =>
        {
            table.HasCheckConstraint(
                "drafts_kind_check",
                "kind is null or kind in ('task', 'reminder', 'meeting')");
            table.HasCheckConstraint("drafts_clarification_round_check", "clarification_round between 0 and 2");
            table.HasCheckConstraint(
                "drafts_time_requires_date",
                "starts_at is null or starts_on is not null");
            table.HasCheckConstraint(
                "drafts_duration_requires_time",
                "duration is null or starts_at is not null");
            table.HasCheckConstraint("drafts_duration_positive", "duration is null or duration > interval '0'");
        });

        builder.HasKey(draft => draft.Id);

        builder.Property(draft => draft.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(draft => draft.TurnId)
            .HasColumnName("turn_id")
            .IsRequired();

        builder.Property(draft => draft.Kind)
            .HasColumnName("kind")
            .HasConversion(
                kind => kind == null ? null : EnumText.ToDb(kind.Value),
                value => value == null ? null : EnumText.DraftKindFromDb(value));

        builder.Property(draft => draft.Description)
            .HasColumnName("description");

        builder.Property(draft => draft.SourceText)
            .HasColumnName("source_text");

        builder.Property(draft => draft.StartsOn)
            .HasColumnName("starts_on")
            .HasColumnType("date");

        builder.Property(draft => draft.StartsAt)
            .HasColumnName("starts_at")
            .HasColumnType("time without time zone");

        builder.Property(draft => draft.Duration)
            .HasColumnName("duration")
            .HasColumnType("interval");

        builder.Property(draft => draft.IsDeadline)
            .HasColumnName("is_deadline")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(draft => draft.ClarificationRound)
            .HasColumnName("clarification_round")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(draft => draft.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(draft => draft.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        // Размерность не фиксируется до выбора EMBEDDING_MODEL/EMBEDDING_DIM.
        builder.Property<Vector?>("Embedding")
            .HasColumnName("embedding")
            .HasColumnType("vector");

        builder.HasOne<Turn>()
            .WithOne(turn => turn.Draft)
            .HasForeignKey<Draft>(draft => draft.TurnId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(draft => draft.TurnId)
            .IsUnique()
            .HasDatabaseName("drafts_turn_id_key");

        builder.Ignore(draft => draft.CanAskClarification);
    }
}
