using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManager.Turns.Domain;

namespace TaskManager.Infrastructure.Persistence.Configurations;

internal sealed class AnswerConfiguration : IEntityTypeConfiguration<Answer>
{
    public void Configure(EntityTypeBuilder<Answer> builder)
    {
        builder.ToTable("answers", table =>
        {
            table.HasCheckConstraint(
                "answers_audio_status_check",
                "audio_status in ('pending', 'ready', 'failed', 'skipped')");
            table.HasCheckConstraint("answers_total_count_check", "total_count is null or total_count >= 0");
        });

        builder.HasKey(answer => answer.Id);

        builder.Property(answer => answer.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(answer => answer.TurnId)
            .HasColumnName("turn_id")
            .IsRequired();

        builder.Property(answer => answer.Text)
            .HasColumnName("text")
            .IsRequired();

        builder.Property(answer => answer.TotalCount)
            .HasColumnName("total_count");

        builder.Property(answer => answer.Warnings)
            .HasColumnName("warnings")
            .HasColumnType("jsonb")
            .HasConversion(JsonStringListConverter.Converter)
            .Metadata.SetValueComparer(JsonStringListConverter.Comparer);

        builder.Property(answer => answer.AudioObjectKey)
            .HasColumnName("audio_object_key");

        builder.Property(answer => answer.AudioStatus)
            .HasColumnName("audio_status")
            .HasConversion(
                status => EnumText.ToDb(status),
                value => EnumText.AudioStatusFromDb(value))
            .HasDefaultValue(AnswerAudioStatus.Skipped)
            // Pending — CLR default (0), но это значимое значение: без явного sentinel
            // EF не вставил бы его и база подставила бы skipped.
            .HasSentinel(AnswerAudioStatus.Skipped)
            .IsRequired();

        builder.Property(answer => answer.AudioDurationMs)
            .HasColumnName("audio_duration_ms");

        builder.Property(answer => answer.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne<Turn>()
            .WithOne()
            .HasForeignKey<Answer>(answer => answer.TurnId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(answer => answer.TurnId)
            .IsUnique()
            .HasDatabaseName("answers_turn_id_key");
    }
}
