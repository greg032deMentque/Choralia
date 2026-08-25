using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class MembershipRequestConfiguration : IEntityTypeConfiguration<MembershipRequest>
{
    public void Configure(EntityTypeBuilder<MembershipRequest> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Status).IsRequired();
        builder.Property(d => d.Message).HasMaxLength(500);
        builder.Property(d => d.DeclineReason).HasMaxLength(500);

        builder.HasOne(d => d.Space)
            .WithMany()
            .HasForeignKey(d => d.SpaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Une seule demande EnAttente (0) par couple (utilisateur, espace) : le filtre porte
        // sur le statut, pas seulement sur IsDeleted — une demande Refusee/Admise/Annulee ne
        // bloque jamais une nouvelle demande par elle-meme (voir la regle des 30 jours,
        // evaluee en service sur HandledAt, pas par un index).
        builder.HasIndex(d => new { d.UserId, d.SpaceId })
            .IsUnique()
            .HasFilter("[Status] = 0 AND [IsDeleted] = 0");

        builder.HasQueryFilter(d => !d.IsDeleted);
    }
}
