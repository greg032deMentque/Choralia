namespace ChoraleBackEnd.ViewModels.AdminDashboard;

/// <summary>Actionable via <c>ChoirStatusEnum</c> — voir <c>AdminChoirsPagedFilterViewModel.Status</c>.</summary>
public sealed class ChoirsKpiViewModel
{
    public int Total { get; set; }
    public int Draft { get; set; }
    public int Published { get; set; }
    public int Cancelled { get; set; }
    public int Archived { get; set; }
}
