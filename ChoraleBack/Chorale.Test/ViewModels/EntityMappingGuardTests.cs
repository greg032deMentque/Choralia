using AutoMapper;
using AutoMapper.Internal;
using ChoraleBackEnd.ViewModels.Songs;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.ViewModels;

/// <summary>
/// Verrouille la regle « aucune cle de rattachement ni champ d'audit n'est mappable depuis le
/// corps d'une requete ».
/// </summary>
/// <remarks>
/// Motif : les services d'ecriture executent leurs gardes d'autorisation sur la valeur LUE EN
/// BASE, puis appellent <c>_mapper.Map(model, entity)</c>. Toute cle de rattachement laissee
/// mappable est donc ecrasee APRES le controle — le contenu change de proprietaire sans qu'aucune
/// garde ne s'en apercoive. Trois profils portaient ce defaut (<c>Song</c>, <c>SongList</c>,
/// <c>Section</c>) ; ce test empeche le quatrieme.
///
/// Un profil ajoute plus tard sans <c>Ignore()</c> fait echouer la suite : c'est le seul moyen
/// d'attraper le defaut, la compilation ne dit rien et la relecture de diff non plus.
/// </remarks>
[TestFixture]
public sealed class EntityMappingGuardTests
{
    /// <summary>Cles designant le conteneur d'une ressource — jamais reprises du client.</summary>
    private static readonly string[] ScopeKeys =
    [
        "ClientId", "ChoirId", "SpaceId", "ChoirOwnerId", "SectionId", "EventId", "SongId"
    ];

    /// <summary>Champs poses par l'infrastructure d'audit, jamais par l'appelant.</summary>
    private static readonly string[] AuditFields =
    [
        "IsDeleted", "CreatedAt", "CreatedByUserId", "UpdatedAt", "UpdatedByUserId"
    ];

    /// <summary>
    /// Entites de liaison pure, exemptees de la regle sur les cles de rattachement.
    /// </summary>
    /// <remarks>
    /// Sur une table de liaison, les cles NE SONT PAS une portee a autoriser : elles sont le
    /// contenu meme de la ligne. Les ignorer rendrait toute creation impossible. La protection
    /// de ces entites appartient au service qui les cree, qui doit autoriser la paire avant de
    /// l'ecrire — pas au profil de mapping.
    ///
    /// Etat constate a l'ecriture de ce test : aucun appelant ne mappe ces deux types dans le
    /// sens ecriture. Le jour ou un chemin de creation apparait, il porte la charge d'autoriser
    /// les deux extremites du lien.
    /// </remarks>
    private static readonly string[] JoinEntities = ["SongListSong", "SectionMember"];

    private static IEnumerable<TypeMap> WriteDirectionMaps()
    {
        var configuration = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(SongViewModel).Assembly),
            NullLoggerFactory.Instance);

        return configuration.Internal()
            .GetAllTypeMaps()
            .Where(map => map.DestinationType.Namespace == "ChoraleBackEnd.Data.Entities");
    }

    /// <summary>
    /// Une propriete n'est reellement ecrite que si AutoMapper l'a appariee a une source.
    /// Une propriete de destination sans source n'est pas un risque : elle n'est jamais posee.
    /// </summary>
    private static PropertyMap? PairedProperty(TypeMap map, string name)
        => map.PropertyMaps.FirstOrDefault(
            propertyMap => propertyMap.DestinationName == name && propertyMap.SourceMember is not null);

    /// <summary>
    /// Un test de garde qui n'enumere rien est vert sans rien prouver.
    /// </summary>
    /// <remarks>
    /// Celui-ci echoue si le filtre de namespace, l'assemblage charge ou l'API d'introspection
    /// d'AutoMapper cesse de remonter les profils — cas ou les deux gardes ci-dessous
    /// deviendraient decoratives sans que personne ne s'en apercoive.
    /// </remarks>
    [Test]
    public void WriteDirectionMaps_AreActuallyEnumerated()
    {
        var maps = WriteDirectionMaps()
            .Select(map => $"{map.SourceType.Name} -> {map.DestinationType.Name}")
            .ToList();

        Assert.That(maps, Is.Not.Empty,
            "Aucun profil d'écriture énuméré : les gardes de ce fichier ne vérifient rien.");
        Assert.That(maps, Does.Contain("SongViewModel -> Song"),
            "Le profil de référence n'est plus énuméré : les gardes ne couvrent plus Song.");
    }

    [Test]
    public void WriteMaps_NeverExposeScopeKeys()
    {
        var violations = WriteDirectionMaps()
            .Where(map => !JoinEntities.Contains(map.DestinationType.Name))
            .SelectMany(map => ScopeKeys
                .Select(key => new { map, key, property = PairedProperty(map, key) })
                .Where(x => x.property is not null && !x.property.Ignored)
                .Select(x => $"{x.map.SourceType.Name} -> {x.map.DestinationType.Name} : {x.key}"))
            .ToList();

        Assert.That(violations, Is.Empty,
            "Ces profils laissent une clé de rattachement mappable depuis le corps de la requête. "
            + "Les gardes d'autorisation s'exécutent sur la valeur stockée, le mapping l'écrase "
            + "ensuite : la ressource change de propriétaire sans contrôle. Ajouter "
            + "`.ForMember(dest => dest.<Clé>, opt => opt.Ignore())` et poser la valeur "
            + "explicitement dans le service, après ses gardes.\n"
            + string.Join("\n", violations));
    }

    [Test]
    public void WriteMaps_NeverExposeAuditFields()
    {
        var violations = WriteDirectionMaps()
            .SelectMany(map => AuditFields
                .Select(field => new { map, field, property = PairedProperty(map, field) })
                .Where(x => x.property is not null && !x.property.Ignored)
                .Select(x => $"{x.map.SourceType.Name} -> {x.map.DestinationType.Name} : {x.field}"))
            .ToList();

        Assert.That(violations, Is.Empty,
            "Ces profils laissent un champ d'audit mappable depuis le corps de la requête. "
            + "L'horodatage, l'auteur et le drapeau de suppression sont posés par "
            + "l'infrastructure, jamais par l'appelant.\n"
            + string.Join("\n", violations));
    }
}
