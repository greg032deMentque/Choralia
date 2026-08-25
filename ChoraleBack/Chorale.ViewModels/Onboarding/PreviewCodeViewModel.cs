using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.Onboarding;

/// <summary>
/// Reponse de <c>GET /api/onboarding/PreviewCode</c> : uniquement le nom et le type de
/// l'espace. Surtout pas le nombre de membres — c'est une donnee de l'espace exposee a un
/// porteur de code non encore admis (decision produit).
/// </summary>
public sealed class PreviewCodeViewModel
{
    public string Name { get; set; } = string.Empty;
    public SpaceTypeEnum SpaceType { get; set; }
}
