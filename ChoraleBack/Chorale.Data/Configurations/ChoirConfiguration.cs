using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class ChoirConfiguration : IEntityTypeConfiguration<Entities.Choir>
{
    public void Configure(EntityTypeBuilder<Entities.Choir> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(150);
        builder.Property(c => c.Description).HasMaxLength(500);
        builder.Property(c => c.ImageUrl).HasMaxLength(500);
        builder.Property(c => c.Status).IsRequired();

        // 0..3 = ChoirStatusEnum (ordinaux persistes). Meme approche que
        // CK_ClientMember_ClientRole : sans cette borne, une valeur hors enum arrivant par un
        // autre chemin qu'EF (import, correctif manuel) serait persistee et sortirait la
        // chorale de tout etat connu par ChoraleEtatHelper.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Choir_Status",
            "[Status] BETWEEN 0 AND 3"));

        builder.HasOne(c => c.Space)
            .WithOne()
            .HasForeignKey<Entities.Choir>(c => c.Id)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Client)
            .WithMany(cl => cl.Choirs)
            .HasForeignKey(c => c.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.ClientId);

        // Sert les lists filtrees par statut (liste transverse Admin notamment), sans
        // devoir scanner toutes les chorales d'un client pour n'en garder qu'une partie.
        builder.HasIndex(c => new { c.ClientId, c.Status });

        builder.HasQueryFilter(c => !c.IsDeleted && !c.Space.IsDeleted);
    }
}
