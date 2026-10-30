using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManager.Turns.Domain;

namespace TaskManager.Infrastructure.Persistence.Configurations;

internal sealed class TurnConfiguration : IEntityTypeConfiguration<Turn>
{
    public void Configure(EntityTypeBuilder<Turn> builder)
    {
        builder.ToTable("turns", table =>
        {
            table.HasCheckConstraint(
                "turns_state_check",
                "state in ('TRANSCRIBING', 'INTERPRETING', 'APPLYING', 'COMPLETED', 'NEEDS_CLARIFICATION', 'FAILED', 'EXPIRED')");
            table.HasCheckConstraint("turns_state_version_check", "state_version >= 1");
        });

        builder.HasKey(turn => turn.Id);

        builder.Property(turn => turn.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(turn => turn.SessionId)
            .HasColumnName("session_id")
            .IsRequired();

        builder.Property(turn => turn.State)
            .HasColumnName("state")
            .HasConversion(
                state => EnumText.ToDb(state),
                value => EnumText.TurnStateFromDb(value))
            .IsRequired();

        // Единственный concurrency token — state_version; CAS-переходы сравнивают
        // state и state_version явно в условии ExecuteUpdate.
        builder.Property(turn => turn.StateVersion)
            .HasColumnName("state_version")
            .HasDefaultValue(Turn.InitialStateVersion)
            .IsConcurrencyToken();

        builder.Property(turn => turn.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .IsRequired();

        builder.Property(turn => turn.Text)
            .HasColumnName("text");

        builder.Property(turn => turn.AudioObjectKey)
            .HasColumnName("audio_object_key");

        builder.Property(turn => turn.SttConfidence)
            .HasColumnName("stt_confidence")
            .HasColumnType("numeric(4,3)");

        builder.Property(turn => turn.Plan)
            .HasColumnName("plan")
            .HasColumnType("jsonb");

        builder.Property(turn => turn.ErrorCode)
            .HasColumnName("error_code");

        builder.Property(turn => turn.DeadlineAt)
            .HasColumnName("deadline_at")
            .IsRequired();

        builder.Property(turn => turn.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(turn => turn.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(turn => turn.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("turns_idempotency_key_key");

        builder.HasIndex(turn => turn.SessionId)
            .IsUnique()
            .HasDatabaseName("turns_one_active")
            .HasFilter("state in ('TRANSCRIBING', 'INTERPRETING', 'APPLYING')");

        builder.HasIndex(turn => new { turn.State, turn.DeadlineAt })
            .HasDatabaseName("turns_deadline");

        builder.HasIndex(turn => turn.CreatedAt)
            .IsDescending(true)
            .HasDatabaseName("turns_recent");

        builder.Ignore(turn => turn.IsActive);
        builder.Ignore(turn => turn.IsTerminal);
    }
}
