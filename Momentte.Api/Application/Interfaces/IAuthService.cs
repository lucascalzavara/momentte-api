using Momentte.Api.Domain.DTOs.Auth;
namespace Momentte.Api.Application.Interfaces;
public interface IAuthService
{
    Task<AuthTokenResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<AuthTokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
}
