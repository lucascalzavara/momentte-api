using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Momentte.Api.Domain.Billing;
using Momentte.Api.Domain.Events;
using Momentte.Api.Application.Interfaces;
namespace Momentte.Api.Controllers;
[ApiController, Route("api/v1/events"), Authorize]
public sealed class EventsController(IEventService service) : ControllerBase
{
    private Guid Owner => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : throw new BillingRuleException(401, "Sessão inválida.");
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await service.List(Owner, ct));
    [HttpPost] public async Task<IActionResult> Create(EventRequest request, CancellationToken ct) => StatusCode(201, await service.Save(Owner, null, request, ct));
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id, EventRequest request, CancellationToken ct) => Ok(await service.Save(Owner, id, request, ct));
    [HttpPost("{id:guid}/activate")] public async Task<IActionResult> Activate(Guid id, ActivateEventRequest request, CancellationToken ct) => Ok(await service.Activate(Owner, id, request.Source, ct));
    [HttpPost("{id:guid}/end")] public async Task<IActionResult> End(Guid id, CancellationToken ct) { await service.End(Owner, id, ct); return NoContent(); }
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await service.Delete(Owner, id, ct); return NoContent(); }
    [HttpDelete("{id:guid}/customization")] public async Task<IActionResult> ClearCustomization(Guid id, CancellationToken ct) { await service.ClearCustomization(Owner, id, ct); return NoContent(); }
    [HttpPost("{id:guid}/media"), RequestSizeLimit(55_000_000)] public async Task<IActionResult> Upload(Guid id, IFormFile file, CancellationToken ct) { await using var input = file.OpenReadStream(); return Ok(await service.Upload(Owner, id, input, false, ct)); }
    [HttpPost("{id:guid}/logo"), RequestSizeLimit(6_000_000)] public async Task<IActionResult> Logo(Guid id, IFormFile file, CancellationToken ct) { await using var input = file.OpenReadStream(); return Ok(await service.Upload(Owner, id, input, true, ct)); }
    [HttpGet("{id:guid}/media")] public async Task<IActionResult> Media(Guid id, CancellationToken ct) => Ok(await service.Media(Owner, id, ct));
    [HttpGet("{id:guid}/media/{mediaId:guid}")] public async Task<IActionResult> Download(Guid id, Guid mediaId, CancellationToken ct) { var file = await service.Download(Owner, id, mediaId, ct); return File(file.Stream, file.Type, mediaId.ToString()); }
    [HttpGet("{id:guid}/logo")] public async Task<IActionResult> DownloadLogo(Guid id, CancellationToken ct) { var file = await service.Download(Owner, id, null, ct); return File(file.Stream, file.Type); }
}
