using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class SpaceMemberConfiguration : IEntityTypeConfiguration<SpaceMember>
{
    public void Configure(EntityTypeBuilder<SpaceMember> builder)
    {
        builder.HasKey(m => m.Id);
        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(m => m.Choir)
            .WithMany(c => c.Members)
            .HasForeignKey(m => m.ChoirId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(m => m.Space)
            .WithMany()
            .HasForeignKey(m => m.SpaceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Property(m => m.Status).IsRequired();
        builder.Property(m => m.Presence);
        builder.HasIndex(m => new { m.UserId, m.SpaceId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasQueryFilter(m => !m.IsDeleted);
    }
}
