using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManager.Events.Domain;
using TaskManager.Turns.Domain;

namespace TaskManager.Infrastructure.Persistence.Configurations;

internal sealed class AnswerEventConfiguration : IEntityTypeConfiguration<AnswerEvent>
{
    public void Configure(EntityTypeBuilder<AnswerEvent> builder)
    {
        builder.ToTable("answer_events");

        builder.HasKey(item => new { item.AnswerId, item.EventId });

        builder.Property(item => item.AnswerId)
            .HasColumnName("answer_id");

        builder.Property(item => item.EventId)
            .HasColumnName("event_id");

        builder.Property(item => item.Position)
            .HasColumnName("position")
            .HasDefaultValue((short)0)
            .IsRequired();

        builder.HasOne<Answer>()
            .WithMany()
            .HasForeignKey(item => item.AnswerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(item => item.EventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
