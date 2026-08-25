namespace ChoraleBackEnd.ViewModels.AdminDashboard;

/// <summary>Actionable via <c>ClientStatusEnum</c> : chaque compteur correspond a une valeur de <see cref="Status"/> a passer en filtre sur la liste des clients.</summary>
public sealed class ClientsKpiViewModel
{
    public int Total { get; set; }
    public int Active { get; set; }
    public int Suspended { get; set; }
    public int Archived { get; set; }
}
