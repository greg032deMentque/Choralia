namespace ChoraleBackEnd.ViewModels.AdminDashboard;

/// <summary>Client (non archive) sans aucun chant et sans aucun membre sur l'ensemble de ses chorales — y compris un client qui n'a encore aucune chorale.</summary>
public sealed class NotStartedClientsKpiViewModel
{
    public int Count { get; set; }
    public List<Guid> ClientIds { get; set; } = [];
}
