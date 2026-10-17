using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Momentte.Api.Domain.Billing;
using Momentte.Api.Domain.Events;
using Momentte.Api.Application.Interfaces;
using Microsoft.AspNetCore.RateLimiting;
using Momentte.Api.Infrastructure.Implementations;
using Momentte.Api.Infrastructure.Interfaces;
namespace Momentte.Api.Controllers;
[ApiController, Route("api/v1/events"), Authorize]
public sealed class EventsController(IEventService service) : ControllerBase
{
    private Guid Owner => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : throw new BillingRuleException(401, "Sessão inválida.");
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await service.List(Owner, ct));
    [HttpPost] public async Task<IActionResult> Create(EventRequest request, CancellationToken ct) => StatusCode(201, await service.Save(Owner, null, request, ct));
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id, EventRequest request, CancellationToken ct) => Ok(await service.Save(Owner, id, request, ct));
    [HttpPost("{id:guid}/activate")] public async Task<IActionResult> Activate(Guid id, ActivateEventRequest request, CancellationToken ct) => Ok(await service.Activate(Owner, id, request.Source, request.AcceptDefaultAppearance, ct));
    [HttpPost("{id:guid}/end")] public async Task<IActionResult> End(Guid id, CancellationToken ct) { await service.End(Owner, id, ct); return NoContent(); }
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await service.Delete(Owner, id, ct); return NoContent(); }
    [HttpDelete("{id:guid}/customization")] public async Task<IActionResult> ClearCustomization(Guid id, CancellationToken ct) { await service.ClearCustomization(Owner, id, ct); return NoContent(); }
    [HttpPost("{id:guid}/media"), RequestSizeLimit(21_000_000)] public async Task<IActionResult> Upload(Guid id, IFormFile file, CancellationToken ct) { await using var input = file.OpenReadStream(); return Ok(await service.Upload(Owner, id, input, false, ct)); }
    [HttpPost("{id:guid}/logo"), RequestSizeLimit(6_000_000)] public async Task<IActionResult> Logo(Guid id, IFormFile file, CancellationToken ct) { await using var input = file.OpenReadStream(); return Ok(await service.Upload(Owner, id, input, true, ct)); }
    [HttpGet("{id:guid}/media")] public async Task<IActionResult> Media(Guid id, CancellationToken ct) => Ok(await service.Media(Owner, id, ct));
    [HttpPost("{id:guid}/images/archive-ticket"), EnableRateLimiting("archives")]
    public async Task<IActionResult> ArchiveTicket(Guid id, [FromServices] EventArchiveTickets tickets, CancellationToken ct)
    {
        var images = (await service.Media(Owner, id, ct)).Where(media => media.HasImage).ToArray();
        if (images.Length == 0) throw new BillingRuleException(404, "Este evento não possui imagens aprovadas para download.");
        return Ok(new { ticket = tickets.Issue(Owner, id) });
    }

    [AllowAnonymous, HttpGet("{id:guid}/images/archive")]
    public async Task Archive(Guid id, [FromQuery] string ticket, [FromServices] EventArchiveTickets tickets,
        [FromServices] IMediaStore store, CancellationToken ct)
    {
        var owner = tickets.Consume(id, ticket);
        // Recheck ownership, expiration and approved media when the browser redeems the ticket.
        var images = (await service.Media(owner, id, ct)).Where(media => media.HasImage).ToArray();
        if (images.Length == 0) throw new BillingRuleException(404, "Este evento não possui imagens aprovadas para download.");
        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition = $"attachment; filename=\"momentte-{id:N}.zip\"";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        try { await EventImageArchive.Write(Response.Body, images, store, ct); }
        catch when (Response.HasStarted) { HttpContext.Abort(); }
    }
    [HttpGet("{id:guid}/media/{mediaId:guid}")] public async Task<IActionResult> Download(Guid id, Guid mediaId, CancellationToken ct) { var file = await service.Download(Owner, id, mediaId, ct); return File(file.Stream, file.Type, mediaId.ToString()); }
    [HttpGet("{id:guid}/logo")] public async Task<IActionResult> DownloadLogo(Guid id, CancellationToken ct) { var file = await service.Download(Owner, id, null, ct); return File(file.Stream, file.Type); }
    [HttpDelete("{id:guid}/logo")]public async Task<IActionResult> RemoveLogo(Guid id,CancellationToken ct){await service.RemoveLogo(Owner,id,ct);return NoContent();}
    [HttpGet("{id:guid}/submissions")] public async Task<IActionResult> Submissions(Guid id,CancellationToken ct)=>Ok(await service.Submissions(Owner,id,null,false,ct));
    [HttpGet("{id:guid}/feed")] public async Task<IActionResult> Feed(Guid id,CancellationToken ct)=>Ok(await service.Submissions(Owner,id,null,true,ct));
    [HttpGet("{id:guid}/submissions/{mediaId:guid}/preview")] public async Task<IActionResult> Preview(Guid id,Guid mediaId,CancellationToken ct){var file=await service.Preview(Owner,id,null,mediaId,ct);return File(file.Stream,file.Type);}
    [HttpPut("{id:guid}/submissions/{mediaId:guid}/moderation")] public async Task<IActionResult> Moderate(Guid id,Guid mediaId,ModerateRequest request,CancellationToken ct){await service.Moderate(Owner,id,mediaId,request.Status,ct);return NoContent();}
    [HttpDelete("{id:guid}/submissions/{mediaId:guid}")] public async Task<IActionResult> DeleteSubmission(Guid id,Guid mediaId,CancellationToken ct){await service.DeleteSubmission(Owner,id,mediaId,ct);return NoContent();}
    [HttpGet("{id:guid}/texts")] public async Task<IActionResult> Texts(Guid id,CancellationToken ct)=>File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(await service.ExportTexts(Owner,id,ct))).ToArray(),"text/csv; charset=utf-8","textos-e-legendas.csv");
}
