using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Momentte.Api.Application.Interfaces;
using Momentte.Api.Domain.DTOs.Auth;
namespace Momentte.Api.Controllers;
[ApiController, Route("api/v1/auth"), AllowAnonymous, EnableRateLimiting("auth")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    /// <summary>Cadastra um usuário e retorna um token de acesso JWT.</summary>
    [HttpPost("register")]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await auth.RegisterAsync(request, cancellationToken));

    /// <summary>Autentica por e-mail e senha e retorna um token de acesso JWT.</summary>
    [HttpPost("login")]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
        => Ok(await auth.LoginAsync(request, cancellationToken));

    /// <summary>Renova a sessão e substitui o refresh token por um novo.</summary>
    [HttpPost("refresh-token")]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken(RefreshTokenRequest request, CancellationToken cancellationToken)
        => Ok(await auth.RefreshTokenAsync(request, cancellationToken));

    /// <summary>Revoga o refresh token da sessão. Operação idempotente.</summary>
    [HttpPost("revoke-token")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeToken(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        await auth.RevokeTokenAsync(request, cancellationToken);
        return NoContent();
    }
}
