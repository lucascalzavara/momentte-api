using Momentte.Api.Domain.Entities;
namespace Momentte.Api.Infrastructure.Interfaces;
public interface IRefreshTokenRepository
{
    // No cadastro, usuário e token são persistidos na mesma transação.
    Task CreateAsync(RefreshToken token, User? newUser = null, CancellationToken cancellationToken = default);
    Task<User?> GetActiveUserAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<bool> RotateAsync(string oldTokenHash, RefreshToken replacement, CancellationToken cancellationToken = default);
    Task RevokeAsync(string tokenHash, CancellationToken cancellationToken = default);
}
