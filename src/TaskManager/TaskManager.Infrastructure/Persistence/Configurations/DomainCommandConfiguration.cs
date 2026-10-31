using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManager.Turns.Domain;

namespace TaskManager.Infrastructure.Persistence.Configurations;

internal sealed class DomainCommandConfiguration : IEntityTypeConfiguration<DomainCommand>
{
    public void Configure(EntityTypeBuilder<DomainCommand> builder)
    {
        builder.ToTable("domain_commands", table =>
        {
            table.HasCheckConstraint(
                "domain_commands_op_check",
                "op in ('event.create', 'event.update')");
            table.HasCheckConstraint(
                "domain_commands_status_check",
                "status in ('pending', 'applied', 'failed')");
            table.HasCheckConstraint("domain_commands_attempts_check", "attempts >= 0");
        });

        builder.HasKey(command => command.Id);

        builder.Property(command => command.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(command => command.TurnId)
            .HasColumnName("turn_id")
            .IsRequired();

        builder.Property(command => command.Seq)
            .HasColumnName("seq")
            .IsRequired();

        builder.Property(command => command.Operation)
            .HasColumnName("op")
            .HasConversion(
                operation => EnumText.ToDb(operation),
                value => EnumText.OperationFromDb(value))
            .IsRequired();

        builder.Property(command => command.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(command => command.Status)
            .HasColumnName("status")
            .HasConversion(
                status => EnumText.ToDb(status),
                value => EnumText.CommandStatusFromDb(value))
            .HasDefaultValue(DomainCommandStatus.Pending)
            .IsRequired();

        builder.Property(command => command.Attempts)
            .HasColumnName("attempts")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(command => command.NextAttemptAt)
            .HasColumnName("next_attempt_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(command => command.LastError)
            .HasColumnName("last_error");

        builder.Property(command => command.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(command => command.AppliedAt)
            .HasColumnName("applied_at");

        builder.HasOne<Turn>()
            .WithMany()
            .HasForeignKey(command => command.TurnId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(command => new { command.TurnId, command.Seq })
            .IsUnique()
            .HasDatabaseName("domain_commands_seq_unique");

        builder.HasIndex(command => new { command.Status, command.NextAttemptAt })
            .HasDatabaseName("domain_commands_pending");
    }
}
