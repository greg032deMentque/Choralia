namespace ChoraleBackEnd.ViewModels.AdminDashboard;

/// <summary>Actionable via <c>AdminSongCatalogPagedFilterViewModel.DuplicatesOnly = true</c> pour les groupes en doublon.</summary>
public sealed class SongsKpiViewModel
{
    public int Total { get; set; }
    public int DuplicateGroups { get; set; }
}
