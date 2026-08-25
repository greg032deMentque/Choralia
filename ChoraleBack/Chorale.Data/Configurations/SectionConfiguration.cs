using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class SectionConfiguration : IEntityTypeConfiguration<Section>
{
    public void Configure(EntityTypeBuilder<Section> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.VoicePart).IsRequired();
        builder.HasOne(p => p.Choir)
            .WithMany(c => c.Sections)
            .HasForeignKey(p => p.ChoirId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(p => p.SectionLeader)
            .WithMany()
            .HasForeignKey(p => p.SectionLeaderId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(p => new { p.ChoirId, p.VoicePart }).IsUnique();
        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
