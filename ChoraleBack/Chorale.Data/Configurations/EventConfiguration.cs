using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Title).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.StartDate).IsRequired();
        builder.Property(e => e.Type).IsRequired();
        builder.Property(e => e.Location).IsRequired().HasMaxLength(300);
        builder.Property(e => e.Status).IsRequired();

        // 1 = EventStatusEnum.Published (ordinal persiste). Un evenement publie sans lieu
        // n'est pas actionnable pour un participant (`04` § Event) : la garde existe
        // dans EventService.ChangeStatusAsync, mais une regle qui n'existe que dans le
        // service finit par etre contournee par un chemin d'ecriture qui l'oublie.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Event_PublishedWithLocation",
            "[Status] <> 1 OR LEN([Location]) > 0"));
        builder.Property(e => e.ClosedAt);
        builder.HasOne(e => e.Choir)
            .WithMany(c => c.Events)
            .HasForeignKey(e => e.ChoirId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Space)
            .WithOne()
            .HasForeignKey<Event>(e => e.Id)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(e => !e.IsDeleted && !e.Space.IsDeleted);
    }
}
