using System;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Services.Events;

/// <summary>
/// Cycle de vie d'un evenement (`04` § Event).
/// </summary>
/// <remarks>
/// `Finished` n'est pas un statut stocke : il se deduit des dates. Ce choix evite une
/// seconde source de verite — il n'y a pas de traitement de fond sur ce projet, donc un
/// `Finished` en base ne serait jamais mis a jour et divergerait des dates des la premiere
/// modification.
///
/// La consequence a proteger est double : un evenement publie doit basculer tout seul, et
/// un evenement annule ne doit <b>pas</b> basculer. Confondre les deux ferait disparaitre
/// un evenement annule de la liste des annules le jour ou sa date passe, sans aucune
/// erreur.
/// </remarks>
[TestFixture]
public sealed class EventStateHelperTests
{
    private static readonly DateTime Past = DateTime.UtcNow.AddDays(-1);
    private static readonly DateTime Future = DateTime.UtcNow.AddDays(1);

    [Test]
    public void Published_FutureDate_StaysPublished()
        => Assert.That(
            EventStateHelper.EffectiveStatus(EventStatusEnum.Published, Future, null),
            Is.EqualTo(EventEffectiveStateEnum.Published));

    [Test]
    public void Published_PastDate_BecomesFinished()
        => Assert.That(
            EventStateHelper.EffectiveStatus(EventStatusEnum.Published, Past, null),
            Is.EqualTo(EventEffectiveStateEnum.Finished));

    [Test]
    public void Published_FutureEndDate_StaysPublished_EvenIfStartDateIsPast()
        => Assert.That(
            EventStateHelper.EffectiveStatus(EventStatusEnum.Published, Past, Future),
            Is.EqualTo(EventEffectiveStateEnum.Published),
            "C'est la date de fin qui tranche quand elle exists.");

    [TestCase(EventStatusEnum.Draft, EventEffectiveStateEnum.Draft)]
    [TestCase(EventStatusEnum.Cancelled, EventEffectiveStateEnum.Cancelled)]
    [TestCase(EventStatusEnum.Archived, EventEffectiveStateEnum.Archived)]
    public void PastDate_DoesNotFlipOtherStatuses(
        EventStatusEnum status, EventEffectiveStateEnum expected)
        => Assert.That(
            EventStateHelper.EffectiveStatus(status, Past, null),
            Is.EqualTo(expected),
            "Une decision humaine ne s'efface pas parce qu'une date passe.");

    [TestCase(EventStatusEnum.Draft, EventStatusEnum.Published)]
    [TestCase(EventStatusEnum.Draft, EventStatusEnum.Archived)]
    [TestCase(EventStatusEnum.Published, EventStatusEnum.Cancelled)]
    [TestCase(EventStatusEnum.Published, EventStatusEnum.Archived)]
    [TestCase(EventStatusEnum.Cancelled, EventStatusEnum.Archived)]
    public void TransitionsAllowed(EventStatusEnum from, EventStatusEnum to)
        => Assert.That(EventStateHelper.IsTransitionAllowed(from, to), Is.True);

    [TestCase(EventStatusEnum.Published, EventStatusEnum.Draft,
        Description = "Depublier ne rend pas invisible ce qui a deja ete vu.")]
    [TestCase(EventStatusEnum.Cancelled, EventStatusEnum.Published,
        Description = "Un evenement annule ne se republie pas : on en cree un nouveau.")]
    [TestCase(EventStatusEnum.Archived, EventStatusEnum.Published)]
    [TestCase(EventStatusEnum.Archived, EventStatusEnum.Draft)]
    [TestCase(EventStatusEnum.Draft, EventStatusEnum.Cancelled,
        Description = "Annuler ce qui n'a jamais ete publie n'a pas de sens : on archive.")]
    public void ForbiddenTransitions(EventStatusEnum from, EventStatusEnum to)
        => Assert.That(EventStateHelper.IsTransitionAllowed(from, to), Is.False);
}
