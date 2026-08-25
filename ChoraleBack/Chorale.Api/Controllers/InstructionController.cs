using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.Instructions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChoraleBackEnd.Api.Controllers;

/// <summary>
/// Consignes de chant (`04` § Instructions et documents).
/// </summary>
/// <remarks>
/// Les policies de methode restent volontairement larges : le droit d'ecriture depend de la
/// <b>voix visee</b> par la consigne — un chef de pupitre ecrit sur la sienne, pas ailleurs —
/// et cela ne s'exprime pas dans un attribut. Le controle fin est dans le service, sur la
/// ressource reellement visee.
/// </remarks>
[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Route("api/instructions")]
public sealed class InstructionController(IInstructionService instructionService) : ControllerBase
{
    [HttpPost("GetPaged")]
    public async Task<ActionResult<PagedListViewModel<InstructionViewModel>>> GetPaged(
        [FromQuery] InstructionPagedFilterViewModel request, CancellationToken cancellationToken = default)
        => Ok(await instructionService.GetPagedAsync(request, cancellationToken));

    [HttpGet("GetById")]
    public async Task<ActionResult<InstructionViewModel>> GetById(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await instructionService.GetByIdAsync(id, cancellationToken));

    [HttpPost("Create")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<ActionResult<InstructionViewModel>> Create(
        [FromBody] CreateInstructionViewModel request, CancellationToken cancellationToken = default)
    {
        var created = await instructionService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("Update")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<ActionResult<InstructionViewModel>> Update(
        [FromBody] UpdateInstructionViewModel request, CancellationToken cancellationToken = default)
        => Ok(await instructionService.UpdateAsync(request, cancellationToken));

    [HttpPost("{id:guid}/Publish")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<ActionResult<InstructionViewModel>> Publish(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await instructionService.PublishAsync(id, cancellationToken));

    [HttpPost("{id:guid}/Archive")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<ActionResult<InstructionViewModel>> Archive(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await instructionService.ArchiveAsync(id, cancellationToken));

    [HttpDelete("Delete")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<IActionResult> Delete(
        Guid id, CancellationToken cancellationToken = default)
    {
        await instructionService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
