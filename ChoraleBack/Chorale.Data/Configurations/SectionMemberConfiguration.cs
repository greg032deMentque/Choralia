using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class SectionMemberConfiguration : IEntityTypeConfiguration<SectionMember>
{
    public void Configure(EntityTypeBuilder<SectionMember> builder)
    {
        builder.HasKey(m => m.Id);
        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(m => m.Section)
            .WithMany(p => p.Members)
            .HasForeignKey(m => m.SectionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.UserId, m.SectionId }).IsUnique();
        // Filtre aligne sur le parent (Pupitre) : sans lui, EF avertit au demarrage
        // (PossibleIncorrectRequiredNavigationWithQueryFilterInteraction) et une jointure
        // requise vers un parent soft-delete produirait des lignes orphelines dans les
        // resultats. L'enfant suit le cycle de vie de son parent.
        builder.HasQueryFilter(mp => !mp.Section.IsDeleted);
    }
}
