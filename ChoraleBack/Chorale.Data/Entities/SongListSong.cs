namespace ChoraleBackEnd.Data.Entities;

public sealed class SongListSong
{
    public Guid Id { get; set; }
    public Guid SongListId { get; set; }
    public Guid SongId { get; set; }
    public int Position { get; set; }

    public SongList SongList { get; set; } = null!;
    public Song Song { get; set; } = null!;
}
