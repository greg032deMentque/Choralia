using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class SpaceMemberRoleConfiguration : IEntityTypeConfiguration<SpaceMemberRole>
{
    public void Configure(EntityTypeBuilder<SpaceMemberRole> builder)
    {
        builder.HasKey(m => m.Id);
        builder.HasOne(m => m.SpaceMember)
            .WithMany()
            .HasForeignKey(m => m.SpaceMemberId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.SpaceMemberId, m.Role }).IsUnique();
        // Filtre aligne sur le parent (SpaceMember) : sans lui, EF avertit au demarrage
        // (PossibleIncorrectRequiredNavigationWithQueryFilterInteraction) et une jointure
        // requise vers un parent soft-delete produirait des lignes orphelines dans les
        // resultats. L'enfant suit le cycle de vie de son parent.
        builder.HasQueryFilter(r => !r.SpaceMember.IsDeleted);
    }
}
