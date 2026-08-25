namespace ChoraleBackEnd.ViewModels.Dashboard;

/// <summary>
/// Indicateurs d'une chorale (`09` § Indicateurs par chorale).
/// </summary>
/// <remarks>
/// Ne contient que ce qui est reellement calculable sur les donnees existantes (`10-D30`).
/// Volontairement absents, faute de source :
/// membres actifs sur 30 jours, taux d'actifs par voix, ecoutes moyennes par chant, chants
/// jamais ecoutes, delai moyen creation vers completude — tous demandent une agregation
/// d'`AnalyticLog` qui n'existe pas ; et le flux d'activite recente, qui demande un
/// journal d'audit exploitable en lecture.
///
/// Ils ne doivent pas apparaitre a l'ecran a zero ou en tiret : un indicateur faux est plus
/// nuisible qu'un indicateur absent.
/// </remarks>
public sealed class ChoirKpiViewModel
{
    /// <summary>Chants actifs du repertoire.</summary>
    public int SongsInRepertoire { get; set; }

    /// <summary>
    /// Chants actifs qui n'ont pas leur partition de reference publiee, ou au moins une
    /// voix attendue sans enregistrement publie (`10-D10`, completude chorale).
    /// </summary>
    public int IncompleteSongs { get; set; }

    /// <summary>Taille de la file d'attente de validation.</summary>
    public int RecordingsPendingReview { get; set; }

    public int Members { get; set; }
    public int InvitedMembers { get; set; }

    /// <summary>Events publies a venir, les plus proches d'abord.</summary>
    public List<NextEventViewModel> UpcomingEvents { get; set; } = [];
}
