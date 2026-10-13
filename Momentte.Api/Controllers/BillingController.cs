using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Momentte.Api.Application.Interfaces;
using Momentte.Api.Domain.Billing;
using Momentte.Api.Infrastructure.Interfaces;
namespace Momentte.Api.Controllers;
[ApiController, Route("api/v1/billing"), Authorize]
public sealed class BillingController(IBillingService service, IMercadoPagoGateway gateway, IConfiguration config) : ControllerBase
{
    private Guid Owner => Guid.TryParse(User.FindFirstValue(config["Billing:Product"] == "alicerce" ? "company_id" : "sub"), out var id) ? id : throw new BillingRuleException(401, "Sessão inválida.");
    private void RequireAdmin() { if (config["Billing:Product"] == "alicerce" && !User.IsInRole("ADMIN")) throw new BillingRuleException(403, "Apenas administradores podem gerenciar o plano da empresa."); }
    [HttpGet] public async Task<IActionResult> Overview(CancellationToken ct) => Ok(await service.Overview(Owner, ct));
    [HttpPost("checkout")] public async Task<IActionResult> Checkout(CheckoutRequest request, CancellationToken ct)
    { RequireAdmin(); return Ok(await service.Checkout(Owner, User.FindFirstValue("email") ?? User.FindFirstValue(ClaimTypes.Email) ?? "", request, ct)); }
    [HttpPost("change-plan")] public async Task<IActionResult> Change(PlanChangeRequest request, CancellationToken ct)
    { RequireAdmin(); await service.ChangePlan(Owner, request.PlanCode, ct); return NoContent(); }
    [HttpPost("reconcile")] public async Task<IActionResult> Reconcile(CancellationToken ct)
    { await service.Reconcile(Owner, ct); return Ok(await service.Overview(Owner, ct)); }
    [AllowAnonymous, HttpPost("mercadopago/webhook")]
    public async Task<IActionResult> Webhook([FromBody] JsonElement payload, CancellationToken ct)
    {
        var id = Request.Query["data.id"].ToString();
        if (!gateway.VerifySignature(id, Request.Headers["x-request-id"].ToString(), Request.Headers["x-signature"].ToString())) return Unauthorized();
        if (!payload.TryGetProperty("data", out var data) || !data.TryGetProperty("id", out var bodyId) || bodyId.ToString().ToLowerInvariant() != id.ToLowerInvariant()) return BadRequest();
        await service.Notify(payload.TryGetProperty("type", out var type) ? type.GetString() ?? "" : "", id, ct);
        return Ok();
    }
}
