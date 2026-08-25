using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.AdminSongs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChoraleBackEnd.Api.Controllers.AdminControllers;

/// <summary>
/// Catalogue transverse des chants pour l'administration generale (lot 4, decision
/// utilisateur : regroupement d'AFFICHAGE uniquement, aucune entite <c>Oeuvre</c>). Lecture
/// seule et sans acces au contenu (partitions, enregistrements) — l'admin voit le catalogue,
/// il n'entre pas dans le contenu.
/// </summary>
[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Authorize(Roles = "Admin")]
[Route("api/admin-songs")]
public sealed class AdminSongController(IAdminSongService adminSongService) : ControllerBase
{
    [HttpPost("GetPagedCatalogue")]
    public async Task<ActionResult<PagedListViewModel<AdminSongCatalogItemViewModel>>> GetPagedCatalogue(
        [FromQuery] AdminSongCatalogPagedFilterViewModel request, CancellationToken cancellationToken = default)
        => Ok(await adminSongService.GetPagedCatalogueAsync(request, cancellationToken));

    [HttpGet("GetGroupChoirs")]
    public async Task<ActionResult<List<AdminSongGroupChoirItemViewModel>>> GetGroupChoirs(
        [FromQuery] string key, CancellationToken cancellationToken = default)
        => Ok(await adminSongService.GetGroupChoirsAsync(key, cancellationToken));
}
