using ChoraleBackEnd.Common.Enums;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Common;

/// <summary>
/// Contrat des ordinaux d'enum. C'est le test le plus rentable du projet.
/// </summary>
/// <remarks>
/// Les enums sont stockes en ENTIER en base (19 colonnes) et exposes en entier au front,
/// qui duplique les memes valeurs dans ses propres files. Reordonner un enum — par
/// exemple remettre <see cref="VoicePartEnum"/> dans l'ordre musical SATB — ne casse ni la
/// compilation ni aucun test fonctionnel : toutes les lignes existantes changeraient
/// simplement de sens, silencieusement. Les sopranos deviendraient des altos.
///
/// Certains ordinaux sont en plus ecrits en dur dans du SQL : le filtre d'index unique de
/// `Scores` (`[Status] = 1`), les CHECK de `Instructions` (`[Scope] = 0..3`,
/// `[Status] <> 1`), et la migration de conversion.
///
/// Ce test est donc le seul endroit ou une permutation ECHOUE. Si tu le vois rouge apres
/// avoir modifie un enum, la reponse n'est jamais de corriger le test : c'est de remettre
/// la valeur, ou d'ecrire la migration de donnees qui accompagne le changement.
/// </remarks>
[TestFixture]
public sealed class EnumOrdinalsTests
{
    [TestCase(VoicePartEnum.Alto, 0)]
    [TestCase(VoicePartEnum.Soprano, 1)]
    [TestCase(VoicePartEnum.Bass, 2)]
    [TestCase(VoicePartEnum.Tenor, 3)]
    public void VoicePart(VoicePartEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));

    [TestCase(UserRoleEnum.Admin, 0)]
    [TestCase(UserRoleEnum.SectionLeader, 1)]
    [TestCase(UserRoleEnum.Singer, 2)]
    [TestCase(UserRoleEnum.Manager, 3)]
    [TestCase(UserRoleEnum.Organizer, 4)]
    [TestCase(UserRoleEnum.Participant, 5)]
    [TestCase(UserRoleEnum.ClientManager, 6)]
    public void Roles(UserRoleEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));

    [TestCase(MemberStatusEnum.Invited, 0)]
    [TestCase(MemberStatusEnum.Active, 1)]
    [TestCase(MemberStatusEnum.Inactive, 2)]
    [TestCase(MemberStatusEnum.Archived, 3)]
    public void StatusMember(MemberStatusEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));

    [TestCase(AttendanceEnum.NoReply, 0)]
    [TestCase(AttendanceEnum.Attending, 1)]
    [TestCase(AttendanceEnum.Maybe, 2)]
    [TestCase(AttendanceEnum.NotAttending, 3)]
    public void Presence(AttendanceEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));

    [TestCase(ScoreStatusEnum.Draft, 0)]
    [TestCase(ScoreStatusEnum.Published, 1)]
    [TestCase(ScoreStatusEnum.Archived, 2)]
    public void StatusScore(ScoreStatusEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));

    [TestCase(RecordingStatusEnum.Draft, 0)]
    [TestCase(RecordingStatusEnum.PendingReview, 1)]
    [TestCase(RecordingStatusEnum.Published, 2)]
    [TestCase(RecordingStatusEnum.Archived, 3)]
    public void StatusRecording(RecordingStatusEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));

    [TestCase(RecordingSourceEnum.RecordedInApp, 0)]
    [TestCase(RecordingSourceEnum.UploadedFile, 1)]
    [TestCase(RecordingSourceEnum.Shared, 2)]
    public void SourceRecording(RecordingSourceEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));

    [TestCase(RecordingTypeEnum.General, 0)]
    [TestCase(RecordingTypeEnum.ByVoicePart, 1)]
    public void TypeRecording(RecordingTypeEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));

    [TestCase(ScoreTypeEnum.General, 0)]
    [TestCase(ScoreTypeEnum.ByVoicePart, 1)]
    public void TypeScore(ScoreTypeEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));

    [TestCase(SongStatusEnum.Active, 0)]
    [TestCase(SongStatusEnum.Archived, 1)]
    public void SongStatus(SongStatusEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));

    [TestCase(SongPriorityEnum.Low, 0)]
    [TestCase(SongPriorityEnum.Normal, 1)]
    [TestCase(SongPriorityEnum.High, 2)]
    public void PrioritySong(SongPriorityEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));

    [TestCase(SongListTypeEnum.Free, 0)]
    [TestCase(SongListTypeEnum.Event, 1)]
    [TestCase(SongListTypeEnum.Season, 2)]
    [TestCase(SongListTypeEnum.Section, 3)]
    public void TypeList(SongListTypeEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));

    [TestCase(SongListStatusEnum.Draft, 0)]
    [TestCase(SongListStatusEnum.Published, 1)]
    [TestCase(SongListStatusEnum.Archived, 2)]
    public void StatusList(SongListStatusEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));

    [TestCase(EventTypeEnum.Concert, 0)]
    [TestCase(EventTypeEnum.Rehearsal, 1)]
    [TestCase(EventTypeEnum.Wedding, 2)]
    [TestCase(EventTypeEnum.Mass, 3)]
    [TestCase(EventTypeEnum.Funeral, 4)]
    [TestCase(EventTypeEnum.Other, 5)]
    public void TypeEvent(EventTypeEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));

    [TestCase(EventStatusEnum.Draft, 0)]
    [TestCase(EventStatusEnum.Published, 1)]
    [TestCase(EventStatusEnum.Cancelled, 2)]
    [TestCase(EventStatusEnum.Archived, 3)]
    public void StatusEvent(EventStatusEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));

    [TestCase(EventEffectiveStateEnum.Draft, 0)]
    [TestCase(EventEffectiveStateEnum.Published, 1)]
    [TestCase(EventEffectiveStateEnum.Finished, 2)]
    [TestCase(EventEffectiveStateEnum.Cancelled, 3)]
    [TestCase(EventEffectiveStateEnum.Archived, 4)]
    public void EffectiveStateEvent(EventEffectiveStateEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));

    [TestCase(SpaceTypeEnum.Choir, 0)]
    [TestCase(SpaceTypeEnum.Event, 1)]
    public void SpaceType(SpaceTypeEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));

    [TestCase(ClientStatusEnum.Active, 0)]
    [TestCase(ClientStatusEnum.Suspended, 1)]
    [TestCase(ClientStatusEnum.Archived, 2)]
    public void StatusClient(ClientStatusEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));

    // InstructionScopeEnum a ete supprime (une consigne n'a plus qu'une cible, son chant) et sa
    // colonne `Scope` avec lui, accompagne de la migration de donnees
    // `InstructionsSongScopeOnly` — le retrait de ces TestCase n'est donc pas un contournement
    // de ce verrou, c'est le pendant de cette migration.

    [TestCase(InstructionStatusEnum.Draft, 0)]
    [TestCase(InstructionStatusEnum.Published, 1)]
    [TestCase(InstructionStatusEnum.Archived, 2)]
    public void StatusInstruction(InstructionStatusEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));

    [TestCase(ChoirStatusEnum.Draft, 0)]
    [TestCase(ChoirStatusEnum.Published, 1)]
    [TestCase(ChoirStatusEnum.Cancelled, 2)]
    [TestCase(ChoirStatusEnum.Archived, 3)]
    public void StatusChoir(ChoirStatusEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));

    [TestCase(MembershipRequestStatusEnum.Pending, 0)]
    [TestCase(MembershipRequestStatusEnum.Approved, 1)]
    [TestCase(MembershipRequestStatusEnum.Declined, 2)]
    [TestCase(MembershipRequestStatusEnum.Cancelled, 3)]
    public void StatusMembershipRequest(MembershipRequestStatusEnum value, int expected) => Assert.That((int)value, Is.EqualTo(expected));
}
