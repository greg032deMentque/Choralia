using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ChoraleBackEnd.Api.Controllers;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.Recordings;
using ChoraleBackEnd.ViewModels.Scores;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Controllers.Contents;

/// <summary>
/// `DownloadAllowed` (D5) etait stocke et renvoye mais jamais applique : les deux
/// endpoints de streaming passaient systematiquement un nom de fichier, ce qui produit
/// `Content-Disposition: attachment`. Autoriser l'ecoute autorisait donc le telechargement,
/// y compris pour un contenu explicitement marque non telechargeable.
///
/// Ces tests verifient la decision faite par le controller. Sans eux, un retour a
/// `File(content, contentType, fileName)` ne ferait echouer aucun test.
/// </summary>
[TestFixture]
public sealed class StreamDownloadTests
{
    private static readonly byte[] Content = [1, 2, 3];

    [Test]
    public async Task Score_DownloadAllowed_CarriesAFileName()
    {
        var controller = new ScoreController(
            new FakeScoreService(downloadAllowed: true));

        var result = await controller.Stream(Guid.NewGuid()) as FileStreamResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.FileDownloadName, Is.EqualTo("score.pdf"),
            "Telechargement autorise : la reponse doit etre servie en attachment.");
    }

    [Test]
    public async Task Score_DownloadForbidden_CarriesNoFileName()
    {
        var controller = new ScoreController(
            new FakeScoreService(downloadAllowed: false));

        var result = await controller.Stream(Guid.NewGuid()) as FileStreamResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.FileDownloadName, Is.Empty,
            "Telechargement interdit : aucun nom de fichier, donc pas de Contenu-Disposition "
            + "attachment. La consultation remaining possible, le telechargement non.");
    }

    [Test]
    public async Task Recording_DownloadAllowed_CarriesAFileName()
    {
        var controller = new RecordingController(
            new FakeRecordingService(downloadAllowed: true));

        var result = await controller.Stream(Guid.NewGuid()) as FileStreamResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.FileDownloadName, Is.EqualTo("audio.mp3"));
        Assert.That(result.EnableRangeProcessing, Is.False,
            "Cette assertion verrouillait l'inverse, sur une justification fausse : le "
            + "lecteur du front recupere la piste entiere en un seul GET "
            + "(`recording.service.ts`, `responseType: 'blob'`) et n'emet aucune requete "
            + "Range. Le transport Blob est la decision retenue, le lien signe ayant ete "
            + "abandonne.");
    }

    [Test]
    public async Task Recording_DownloadForbidden_CarriesNoFileName()
    {
        var controller = new RecordingController(
            new FakeRecordingService(downloadAllowed: false));

        var result = await controller.Stream(Guid.NewGuid()) as FileStreamResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.FileDownloadName, Is.Empty);
        Assert.That(result.EnableRangeProcessing, Is.False,
            "Meme raison que ci-dessus : aucune requete Range n'est emise par le front, quel "
            + "que soit le droit de telechargement.");
    }

    private sealed class FakeScoreService(bool downloadAllowed) : IScoreService
    {
        public Task<(Stream Content, string ContentType, string FileName, bool DownloadAllowed)>
            StreamAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<(Stream, string, string, bool)>(
                (new MemoryStream(Content), "application/pdf", "score.pdf", downloadAllowed));

        public Task<PagedListViewModel<ScoreViewModel>> GetPagedAsync(
            ScorePagedFilterViewModel filter, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<PagedListViewModel<ScoreViewModel>> GetPagedBySongAsync(
            ScoreBySongFilterViewModel filter, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ScoreViewModel> GetByIdAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ScoreViewModel> CreateAsync(CreateScoreViewModel model, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ScoreViewModel> UpdateAsync(Guid id, UpdateScoreViewModel model, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ScoreViewModel> PublishAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ScoreViewModel> ArchiveAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ScoreViewModel> RestoreAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeRecordingService(bool downloadAllowed) : IRecordingService
    {
        public Task<(Stream Content, string ContentType, string FileName, bool DownloadAllowed)>
            StreamAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<(Stream, string, string, bool)>(
                (new MemoryStream(Content), "audio/mpeg", "audio.mp3", downloadAllowed));

        public Task<PagedListViewModel<RecordingViewModel>> GetPagedAsync(
            RecordingPagedFilterViewModel filter, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<PagedListViewModel<RecordingViewModel>> GetPagedBySongAsync(
            RecordingBySongFilterViewModel filter, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<RecordingViewModel> GetByIdAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<RecordingViewModel> CreateAsync(CreateRecordingViewModel model, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<RecordingViewModel> UpdateAsync(Guid id, UpdateRecordingViewModel model, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<RecordingViewModel> SubmitForReviewAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<RecordingViewModel> PublishAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<RecordingViewModel> RejectAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<RecordingViewModel> ArchiveAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<RecordingViewModel> RestoreAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<List<PlaylistTrackViewModel>> GetEventPlaylistByVoicePartAsync(
            Guid eventId, VoicePartEnum voicePart, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
