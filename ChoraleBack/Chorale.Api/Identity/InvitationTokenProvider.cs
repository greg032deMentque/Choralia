using ChoraleBackEnd.Data.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ChoraleBackEnd.Api.Identity;

/// <summary>
/// Fournisseur du jeton d'invitation, enregistre sous
/// <c>AccountTokenConstants.InvitationTokenProvider</c>.
/// </summary>
/// <remarks>
/// Herite du fournisseur DataProtection standard : meme construction, meme validation de
/// security stamp — donc meme consommation unique (poser le mot de passe change le stamp et
/// invalide le lien). Seules les options changent, ce qui suffit a lui donner sa duree de vie
/// propre. <c>IOptions&lt;T&gt;</c> etant covariant, les options derivees sont acceptees
/// telles quelles par le constructeur de base.
/// </remarks>
public sealed class InvitationTokenProvider : DataProtectorTokenProvider<User>
{
    public InvitationTokenProvider(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<InvitationTokenProviderOptions> options,
        ILogger<DataProtectorTokenProvider<User>> logger)
        : base(dataProtectionProvider, options, logger)
    {
    }
}
