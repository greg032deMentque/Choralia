namespace ChoraleBackEnd.Api.Middleware;

/// <summary>
/// Pose les en-tetes de securite communs a toutes les reponses de l'API, y compris les
/// reponses d'erreur.
/// </summary>
/// <remarks>
/// Ce que chaque en-tete evite ici, concretement :
/// <list type="bullet">
/// <item><c>X-Content-Type-Options: nosniff</c> — <c>ScoreController.Stream</c> et
/// <c>RecordingController.Stream</c> reservent <b>inline</b> des fichiers deposes par des
/// utilisateurs. Le <c>Content-Type</c> renvoye vient de notre liste blanche, mais sans cet
/// en-tete un navigateur peut renifler le contenu et requalifier un « PDF » en HTML, donc
/// executer le script qu'il contient : XSS stocke (OWASP A03). Le controle des octets
/// d'en-tete (<c>FileSignatureHelper</c>) ferme la meme porte a l'upload ; les deux sont
/// complementaires, aucun ne remplace l'autre.</item>
/// <item><c>default-src 'none'</c> — l'API ne sert aucun HTML applicatif, seulement du JSON
/// et des fichiers. Un document servi par elle ne doit donc rien charger : ni script, ni
/// image, ni requete sortante.</item>
/// <item><c>frame-ancestors 'self'</c> — le front n'encadre jamais ces URL directement : il
/// telecharge le fichier via <c>HttpClient</c> puis affiche un <c>blob:</c>
/// (<c>score.service.ts</c>, <c>responseType: 'blob'</c>), qui n'est pas soumis a cet
/// en-tete. La restriction ne coute donc rien au parcours et interdit a un tiers d'encadrer
/// la reponse brute.</item>
/// <item><c>base-uri</c> et <c>form-action</c> a <c>'none'</c> — <c>default-src</c> ne les
/// couvre pas ; sans eux, un document servi par l'API pourrait reecrire ses URL relatives ou
/// poster vers l'exterieur.</item>
/// </list>
///
/// Choix ecarte : la directive CSP <c>sandbox</c>. Elle s'appliquerait au visualiseur PDF du
/// navigateur et risquerait de casser l'affichage meme des partitions. Le bac a sable de
/// l'affichage releve de celui qui encadre (l'<c>&lt;iframe sandbox&gt;</c> du front), pas de
/// la reponse.
///
/// Choix ecarte : <c>X-Frame-Options</c>. <c>frame-ancestors</c> le remplace sur tous les
/// navigateurs cibles, et deux en-tetes qui disent la meme chose finissent par se contredire
/// a la premiere evolution.
/// </remarks>
public sealed class SecurityHeadersMiddleware
{
    private const string ContentTypeOptionsHeader = "X-Content-Type-Options";
    private const string ContentTypeOptionsValue = "nosniff";

    private const string ContentSecurityPolicyHeader = "Content-Security-Policy";
    private const string ContentSecurityPolicyValue =
        "default-src 'none'; frame-ancestors 'self'; base-uri 'none'; form-action 'none'";

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Affectation par indexeur, et non Append : re-executee, elle remplace au lieu
        // d'empiler deux valeurs dans le meme en-tete.
        var headers = context.Response.Headers;
        headers[ContentTypeOptionsHeader] = ContentTypeOptionsValue;
        headers[ContentSecurityPolicyHeader] = ContentSecurityPolicyValue;

        return _next(context);
    }
}
