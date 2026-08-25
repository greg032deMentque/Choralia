using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.Clients;

/// <summary>
/// Reserve a l'administration generale (`02` § Matrice, ligne « Fixer les limites de
/// service »). Un responsable client les consulte, ne les modifie pas.
/// </summary>
public sealed class UpdateClientLimitsViewModel
{
    [Required]
    public Guid Id { get; set; }

    [Range(0, int.MaxValue)]
    public int ChoirLimit { get; set; }

    [Range(0, int.MaxValue)]
    public int MemberLimit { get; set; }

    [Range(0, long.MaxValue)]
    public long StorageQuotaBytes { get; set; }

    [Range(0, long.MaxValue)]
    public long MaxFileSizeBytes { get; set; }
}
