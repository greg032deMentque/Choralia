using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class SongListSongConfiguration : IEntityTypeConfiguration<SongListSong>
{
    public void Configure(EntityTypeBuilder<SongListSong> builder)
    {
        builder.HasKey(dc => dc.Id);
        builder.HasOne(dc => dc.SongList)
            .WithMany(d => d.SongListSongs)
            .HasForeignKey(dc => dc.SongListId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(dc => dc.Song)
            .WithMany(c => c.SongListSongs)
            .HasForeignKey(dc => dc.SongId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(dc => new { dc.SongListId, dc.SongId }).IsUnique();
        // Filtre aligne sur le parent (SongList + Chant) : sans lui, EF avertit au demarrage
        // (PossibleIncorrectRequiredNavigationWithQueryFilterInteraction) et une jointure
        // requise vers un parent soft-delete produirait des lignes orphelines dans les
        // resultats. L'enfant suit le cycle de vie de son parent.
        builder.HasQueryFilter(lc => !lc.SongList.IsDeleted && !lc.Song.IsDeleted);
    }
}
