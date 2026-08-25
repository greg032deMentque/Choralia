using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.AdminSongs;

/// <summary>
/// Detail depliable d'un groupe du catalogue (lot 4) : une ligne par chorale portant ce
/// chant. Volontairement non paginee — bornee par nature au nombre de chorales du groupe,
/// jamais un GetAll transverse.
/// </summary>
public sealed class AdminSongGroupChoirItemViewModel
{
    public Guid ChoirId { get; set; }
    public string ChoirName { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public SongStatusEnum SongStatus { get; set; }
    public DateTime CreationDate { get; set; }
}
