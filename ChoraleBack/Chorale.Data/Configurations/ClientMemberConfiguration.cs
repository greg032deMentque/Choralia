using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class ClientMemberConfiguration : IEntityTypeConfiguration<ClientMember>
{
    public void Configure(EntityTypeBuilder<ClientMember> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Role).IsRequired();

        // 6 = UserRoleEnum.ClientManager (ordinal persiste). UserRoleEnum sert trois
        // scopes a la fois : sans cette borne, `Role = Singer` serait storable ici — et
        // surtout `Role = Admin` (ordinal 0, valeur par defaut de tout int) : une ligne mal
        // initialisee deviendrait un administrateur global silencieux.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_ClientMember_ClientRole",
            "[Role] = 6"));

        builder.HasOne(m => m.Client)
            .WithMany(c => c.Members)
            .HasForeignKey(m => m.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Un utilisateur ne detient un role donne qu'une fois par client.
        builder.HasIndex(m => new { m.ClientId, m.UserId, m.Role })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        // Resolution du role a chaque requete : l'index sur UserId sert le chemin chaud.
        builder.HasIndex(m => m.UserId);

        builder.HasQueryFilter(m => !m.IsDeleted);
    }
}
