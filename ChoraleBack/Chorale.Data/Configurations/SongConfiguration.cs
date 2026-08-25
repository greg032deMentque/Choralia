using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class SongConfiguration : IEntityTypeConfiguration<Song>
{
    public void Configure(EntityTypeBuilder<Song> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Title).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Status).IsRequired();
        builder.Property(c => c.Author).HasMaxLength(150);
        builder.Property(c => c.Composer).HasMaxLength(150);
        builder.Property(c => c.Language).HasMaxLength(100);
        builder.Property(c => c.WorkingKey).HasMaxLength(100);
        builder.Property(c => c.Priority);
        builder.Property(c => c.PreparationNotes).HasMaxLength(2000);
        builder.HasOne(c => c.Choir)
            .WithMany()
            .HasForeignKey(c => c.ChoirId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
