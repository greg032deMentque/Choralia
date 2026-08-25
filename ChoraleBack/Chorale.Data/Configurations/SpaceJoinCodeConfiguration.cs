using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class SpaceJoinCodeConfiguration : IEntityTypeConfiguration<SpaceJoinCode>
{
    public void Configure(EntityTypeBuilder<SpaceJoinCode> builder)
    {
        builder.HasKey(c => c.Id);

        // Collation binaire explicite : la comparaison du code ne doit dependre ni de la
        // culture ni de la collation par defaut du serveur SQL (prod sous Ubuntu, dev sous
        // Windows peuvent differer). Sans elle, une collation par defaut insensible a la
        // casse rendrait "ABCD-2345" et "abcd-2345" indiscernables en base.
        builder.Property(c => c.Code)
            .IsRequired()
            .HasMaxLength(9)
            .UseCollation("Latin1_General_100_BIN2");

        builder.Property(c => c.ExpiresAt).IsRequired();
        builder.Property(c => c.IsActive).IsRequired();

        builder.HasOne(c => c.Space)
            .WithMany()
            .HasForeignKey(c => c.SpaceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Un seul code actif par espace, et un code actif ne peut jamais coincider avec un
        // autre code actif ailleurs (le code EST le secret partage, sa valeur doit rester
        // globalement resolvable sans ambiguite tant qu'il est actif).
        builder.HasIndex(c => c.Code)
            .IsUnique()
            .HasFilter("[IsActive] = 1 AND [IsDeleted] = 0");
        builder.HasIndex(c => c.SpaceId)
            .IsUnique()
            .HasFilter("[IsActive] = 1 AND [IsDeleted] = 0");

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
