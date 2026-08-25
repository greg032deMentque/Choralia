using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.Data.Entities;

public sealed class SongVoicePart
{
    public Guid Id { get; set; }
    public Guid SongId { get; set; }
    public VoicePartEnum VoicePart { get; set; }

    public Song Song { get; set; } = null!;
}
