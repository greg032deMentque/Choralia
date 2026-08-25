namespace ChoraleBackEnd.ViewModels.Dashboard;

public sealed class NextEventViewModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }

    public int Targets { get; set; }
    public int Responses { get; set; }

    /// <summary>
    /// Responses recues sur membres cibles (`09`). Null quand personne n'est target : un taux
    /// sur zero target n'a pas de sens, et l'afficher a 0 % ferait croire a une absence de
    /// reponse plutot qu'a une absence de destinataire.
    /// </summary>
    public int? ResponseRate { get; set; }
}
