using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class SpaceConfiguration : IEntityTypeConfiguration<Space>
{
    public void Configure(EntityTypeBuilder<Space> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.SpaceType).IsRequired();
        builder.Property(e => e.ClientId).IsRequired();
        builder.HasOne(e => e.Client)
            .WithMany()
            .HasForeignKey(e => e.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.ClientId, e.SpaceType });

        // Filtre aligne sur le parent (Client) : sans lui, EF avertit au demarrage
        // (PossibleIncorrectRequiredNavigationWithQueryFilterInteraction) et une jointure
        // requise vers un client soft-delete produirait des lignes orphelines dans les
        // resultats. Espace n'a pas de filtre sur son propre IsDeleted (les services qui en
        // ont besoin le filtrent explicitement au cas par cas) : celui-ci porte uniquement
        // sur le cycle de vie du client de rattachement.
        builder.HasQueryFilter(e => !e.Client.IsDeleted);
    }
}
