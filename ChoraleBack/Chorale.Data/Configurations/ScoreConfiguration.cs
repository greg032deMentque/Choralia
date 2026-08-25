using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class ScoreConfiguration : IEntityTypeConfiguration<Score>
{
    public void Configure(EntityTypeBuilder<Score> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Type).IsRequired();
        builder.Property(p => p.TargetVoicePart);
        builder.Property(p => p.Version).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Status).IsRequired();
        builder.Property(p => p.FilePath).IsRequired().HasMaxLength(500);
        builder.Property(p => p.OriginalFileName).HasMaxLength(260);
        builder.HasOne(p => p.Song)
            .WithMany(c => c.Scores)
            .HasForeignKey(p => p.SongId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Owner)
            .WithMany()
            .HasForeignKey(p => p.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
        // Une seule partition de reference publiee par (chant, type, voix).
        // Le filtre doit exclure les lignes soft-deletees : sans `IsDeleted = 0`, une
        // partition publiee puis archivee logiquement continue d'occuper le creneau et
        // empeche la publication de la suivante.
        builder.HasIndex(p => new { p.SongId, p.Type, p.TargetVoicePart })
            .IsUnique()
            // 1 = ScoreStatusEnum.Published. Les enums sont stockes en entier : toute
            // reorganisation de l'enum doit s'accompagner d'une migration de ce filtre.
            .HasFilter("[Status] = 1 AND [IsDeleted] = 0");
        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
