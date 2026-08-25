using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class SongVoicePartConfiguration : IEntityTypeConfiguration<SongVoicePart>
{
    public void Configure(EntityTypeBuilder<SongVoicePart> builder)
    {
        builder.HasKey(cv => cv.Id);
        builder.Property(cv => cv.VoicePart).IsRequired();
        builder.HasOne(cv => cv.Song)
            .WithMany(c => c.SongVoicePart)
            .HasForeignKey(cv => cv.SongId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(cv => new { cv.SongId, cv.VoicePart }).IsUnique();
        // Filtre aligne sur le parent (Chant) : sans lui, EF avertit au demarrage
        // (PossibleIncorrectRequiredNavigationWithQueryFilterInteraction) et une jointure
        // requise vers un parent soft-delete produirait des lignes orphelines dans les
        // resultats. L'enfant suit le cycle de vie de son parent.
        builder.HasQueryFilter(cv => !cv.Song.IsDeleted);
    }
}
