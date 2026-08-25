using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(150);
        builder.Property(c => c.ContactName).HasMaxLength(150);
        builder.Property(c => c.ContactEmail).HasMaxLength(256);
        builder.Property(c => c.Status).IsRequired();

        // Pas d'unicite sur le nom : c'est un libelle d'exploitation, pas une cle. Deux
        // clients distincts peuvent legitimement porter le meme nom commercial (`04` §
        // Client). L'unicite exigeait jusqu'ici une gymnastique de renommage a la creation
        // d'un client dont le nom existait deja, sans aucune raison metier de le faire.

        // Les plafonds sont des entiers positifs. Un plafond negatif passerait sinon
        // silencieusement et refuserait toute creation.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Client_PositiveLimits",
                "[ChoirLimit] >= 0 AND [MemberLimit] >= 0 "
                + "AND [StorageQuotaBytes] >= 0 AND [MaxFileSizeBytes] >= 0");
        });

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
