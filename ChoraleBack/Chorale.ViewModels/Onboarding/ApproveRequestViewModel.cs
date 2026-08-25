using System.ComponentModel.DataAnnotations;
using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.Onboarding;

/// <summary>
/// Admission = affectation (`04`) : voix principale et role sont exiges dans la meme
/// operation, jamais une admission "nue" qui produirait un membre invalide.
/// </summary>
public sealed class ApproveRequestViewModel
{
    [Required]
    public VoicePartEnum? PrimaryVoicePart { get; set; }

    [Required]
    public UserRoleEnum? Role { get; set; }
}
