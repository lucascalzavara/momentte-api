using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Momentte.Api.Application.Interfaces;
using Momentte.Api.Domain.Billing;
namespace Momentte.Api.Controllers;
[ApiController,Authorize,Route("api/v1/participation")]
public sealed class ParticipationController(IEventService service):ControllerBase
{
    private Guid UserId=>Guid.TryParse(User.FindFirstValue("sub"),out var id)?id:throw new BillingRuleException(401,"Entre na sua conta para participar.");
    [HttpGet] public async Task<IActionResult> Mine(CancellationToken ct)=>Ok(await service.Participations(UserId,ct));
    [AllowAnonymous,HttpGet("{code}")] public async Task<IActionResult> Info(string code,CancellationToken ct)=>Ok(await service.PublicInfo(code,ct));
    [HttpPost("{code}/join")] public async Task<IActionResult> Join(string code,CancellationToken ct){await service.Join(UserId,code,ct);return Ok(await service.PublicInfo(code,ct));}
    [HttpGet("{code}/submissions")] public async Task<IActionResult> Mine(string code,CancellationToken ct)=>Ok(await service.Submissions(UserId,null,code,false,ct));
    [HttpPost("{code}/submissions"),RequestSizeLimit(21_000_000),EnableRateLimiting("submissions")]
    public async Task<IActionResult> Submit(string code,[FromForm]string? text,IFormFile? file,[FromForm]bool showName,CancellationToken ct)
    {await using var input=file?.OpenReadStream();return StatusCode(201,await service.Submit(UserId,code,text,input,showName,ct));}
    [HttpGet("{code}/submissions/{mediaId:guid}/preview")] public async Task<IActionResult> Preview(string code,Guid mediaId,CancellationToken ct){var file=await service.Preview(UserId,null,code,mediaId,ct);return File(file.Stream,file.Type);}
    [HttpGet("{code}/logo")] public async Task<IActionResult> Logo(string code,CancellationToken ct){var file=await service.PublicLogo(UserId,code,ct);return File(file.Stream,file.Type);}
}
