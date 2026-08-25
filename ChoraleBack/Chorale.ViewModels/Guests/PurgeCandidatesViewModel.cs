namespace ChoraleBackEnd.ViewModels.Guests;

/// <summary>
/// Apercu, sans purger, des comptes invites qui seraient concernes par
/// <c>PurgeInactiveGuestsAsync</c>. Le nombre annonce ici et le nombre reellement purge
/// peuvent diverger si un compte est revendique (rattache a un espace) entre l'apercu et
/// l'execution — la purge recompte toujours au moment de l'action, jamais a partir de cet
/// apercu.
/// </summary>
public sealed class PurgeCandidatesViewModel
{
    public int Count { get; set; }

    /// <summary>true si d'autres candidats existent au-dela du lot charge (voir <c>PurgeBatchSize</c>).</summary>
    public bool HasMore { get; set; }

    public List<PurgeCandidateItemViewModel> Candidates { get; set; } = [];
}
