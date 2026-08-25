using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.Onboarding;

public sealed class DeclineRequestViewModel
{
    /// <summary>Motif interne : jamais transmis au demandeur (decision produit).</summary>
    [MaxLength(500)]
    public string? DeclineReason { get; set; }
}
