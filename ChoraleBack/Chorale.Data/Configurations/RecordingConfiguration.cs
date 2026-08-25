using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class RecordingConfiguration : IEntityTypeConfiguration<Recording>
{
    public void Configure(EntityTypeBuilder<Recording> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Type).IsRequired();
        builder.Property(e => e.TargetVoicePart);
        builder.Property(e => e.Status).IsRequired();
        builder.Property(e => e.Source).IsRequired();
        builder.Property(e => e.ContentOwner).IsRequired().HasMaxLength(200);
        builder.Property(e => e.FilePath).IsRequired().HasMaxLength(500);
        builder.Property(e => e.OriginalFileName).HasMaxLength(260);
        builder.HasOne(e => e.Song)
            .WithMany(c => c.Recordings)
            .HasForeignKey(e => e.SongId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.ChoirOwner)
            .WithMany()
            .HasForeignKey(e => e.ChoirOwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Creator)
            .WithMany()
            .HasForeignKey(e => e.CreatorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
