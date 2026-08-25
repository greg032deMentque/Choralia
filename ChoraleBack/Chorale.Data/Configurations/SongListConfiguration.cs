using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class SongListConfiguration : IEntityTypeConfiguration<SongList>
{
    public void Configure(EntityTypeBuilder<SongList> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Name).IsRequired().HasMaxLength(150);
        builder.Property(d => d.Description).HasMaxLength(500);
        builder.Property(d => d.Type).IsRequired();
        builder.Property(d => d.Status).IsRequired();
        builder.Property(d => d.OwnerUserId).IsRequired();
        builder.HasOne(d => d.Choir)
            .WithMany(c => c.SongLists)
            .HasForeignKey(d => d.ChoirId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(d => d.Section)
            .WithMany(p => p.SongLists)
            .HasForeignKey(d => d.SectionId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(d => d.Event)
            .WithMany(e => e.SongLists)
            .HasForeignKey(d => d.EventId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(d => d.Owner)
            .WithMany()
            .HasForeignKey(d => d.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(d => d.CreatedBy)
            .WithMany()
            .HasForeignKey(d => d.CreatedById)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasQueryFilter(d => !d.IsDeleted);
    }
}
