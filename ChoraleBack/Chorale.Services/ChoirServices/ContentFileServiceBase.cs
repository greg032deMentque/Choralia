using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using Microsoft.AspNetCore.Http;

namespace ChoraleBackEnd.Services.ChoirServices;

/// <summary>
/// Stockage d'un fichier de contenu de chorale. Les partitions et les enregistrements
/// n'acceptent pas les memes formats, mais partagent toute la mecanique : validation par
/// octets d'en-tete, ecriture sous nom genere, suppression, ouverture en lecture.
/// </summary>
public interface IContentFileService
{
    /// <summary>
    /// Valide extension, Content-Type et octets d'en-tete. A appeler AVANT
    /// <see cref="SaveAsync"/> : les deux lisent le flux, chacun via sa propre vue.
    /// </summary>
    void EnsureAllowedFormat(IFormFile file);

    /// <summary>
    /// Ecrit le fichier sous un nom genere et retourne ce nom, a stocker dans
    /// <c>FilePath</c>. Ne valide rien : <see cref="EnsureAllowedFormat"/> et le controle
    /// de quota passent avant.
    /// </summary>
    Task<string> SaveAsync(IFormFile file, CancellationToken ct = default);

    /// <summary>Supprime le fichier s'il existe. Silencieux s'il a deja disparu.</summary>
    void Delete(string storedFileName);

    /// <summary>
    /// Ouvre le fichier en lecture. Leve <c>404</c> si le fichier reference en base n'existe
    /// plus sur disque.
    /// </summary>
    (Stream Content, string ContentType, string FileName) OpenForDownload(
        string storedFileName, string? originalFileName);
}

/// <remarks>
/// N'herite deliberement PAS de <c>BaseService</c>, meme justification que
/// <see cref="SectionVoicePartLookupService"/> : ne touche ni l'utilisateur courant, ni le
/// <c>DbContext</c>. Les regles d'acces sont portees par
/// <see cref="IChoirAuthorizationService"/> et par les services d'autorisation de chaque
/// contenu ; elles s'appliquent avant tout appel d'ici.
/// </remarks>
public abstract class ContentFileServiceBase : IContentFileService
{
    private readonly IPathService _pathService;

    protected ContentFileServiceBase(IPathService pathService)
    {
        _pathService = pathService;
    }

    /// <summary>Extensions acceptees, et pour chacune les Content-Type que le client peut annoncer.</summary>
    protected abstract IReadOnlyDictionary<string, string[]> AllowedFormats { get; }

    /// <summary>Message d'erreur listant les formats acceptes, propre au type de contenu.</summary>
    protected abstract string MessageFormatNotAllowed { get; }

    /// <summary>Message du <c>404</c> quand le fichier reference en base a disparu du disque.</summary>
    protected abstract string MessageFileNotFound { get; }

    public void EnsureAllowedFormat(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName);
        if (!AllowedFormats.TryGetValue(extension, out var allowedContentTypes)
            || !allowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new CustomException(HttpStatusCode.BadRequest, MessageFormatNotAllowed);
        }

        // Extension et Content-Type viennent tous deux du client : un HTML renomme en .pdf et
        // annonce application/pdf franchit ces deux controles, puis est reservi inline par
        // ScoreController.Stream — XSS stocke. Meme mecanique cote audio avec un .mp3
        // annonce audio/mpeg. Seuls les octets d'en-tete font foi.
        // Le flux est ouvert ici et referme ici : la copie sur disque de SaveAsync ouvre sa
        // propre vue du fichier, elle ne reprend pas celle-ci en cours de lecture.
        using var content = file.OpenReadStream();
        if (!FileSignatureHelper.MatchesExtension(extension, content))
            throw new CustomException(HttpStatusCode.BadRequest, MessageFormatNotAllowed);
    }

    public async Task<string> SaveAsync(IFormFile file, CancellationToken ct = default)
    {
        var fileName = $"{ChoraleDbContext.NewIdGuid()}{Path.GetExtension(file.FileName)}";
        var path = _pathService.GetFilePath(fileName);

        await using (var stream = File.Create(path))
        {
            await file.CopyToAsync(stream, ct);
        }

        return fileName;
    }

    public void Delete(string storedFileName)
    {
        var path = _pathService.GetFilePath(storedFileName);
        if (File.Exists(path))
            File.Delete(path);
    }

    public (Stream Content, string ContentType, string FileName) OpenForDownload(
        string storedFileName, string? originalFileName)
    {
        var path = _pathService.GetFilePath(storedFileName);
        if (!File.Exists(path))
            throw new CustomException(HttpStatusCode.NotFound, MessageFileNotFound);

        var stream = File.OpenRead(path);
        var contentType = ResolveContentType(storedFileName);
        // OriginalFileName vient de l'utilisateur : le renvoyer brut dans
        // Contenu-Disposition permettrait une injection d'en-tete HTTP.
        var fileName = _pathService.SanitizeFileName(originalFileName ?? Path.GetFileName(path));
        return (stream, contentType, fileName);
    }

    private string ResolveContentType(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return AllowedFormats.TryGetValue(extension, out var contentTypes)
            ? contentTypes[0]
            : "application/octet-stream";
    }
}
