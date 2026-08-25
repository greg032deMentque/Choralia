using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Token).IsRequired().HasMaxLength(256);
        builder.HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => r.Token).IsUnique();
        // Filtre aligne sur le parent (User) : sans lui, EF avertit au demarrage
        // (PossibleIncorrectRequiredNavigationWithQueryFilterInteraction) et une jointure
        // requise vers un parent soft-delete produirait des lignes orphelines dans les
        // resultats. L'enfant suit le cycle de vie de son parent.
        builder.HasQueryFilter(t => !t.User.IsDeleted);
    }
}
