using Momentte.Api.Application.Interfaces;
using Momentte.Api.Domain.DTOs.Auth;
using Momentte.Api.Domain.Entities;
using Momentte.Api.Domain.Exceptions;
using Momentte.Api.Infrastructure.Interfaces;
using System.Security.Cryptography;
using System.Text;
namespace Momentte.Api.Application.Implementations;
public sealed class AuthService(IUserRepository users, IPasswordHasher passwords, ITokenService tokens,
    IRefreshTokenRepository refreshTokens, IConfiguration configuration) : IAuthService
{
    // Mesmo custo de verificação quando o e-mail não existe; não expõe o motivo do login inválido.
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString(), workFactor: 12);

    public async Task<AuthTokenResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await users.GetByEmailAsync(email, cancellationToken) is not null)
            throw new ConflictException("Já existe um usuário cadastrado com este e-mail.");
        var now = DateTime.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Name = request.Name.Trim(), Email = email,
            PasswordHash = passwords.HashPassword(request.Password), CreatedAt = now, UpdatedAt = now };
        // Prepara a resposta antes de persistir para evitar cadastro parcial se a emissão falhar.
        var (response, refresh) = GenerateTokens(user);
        await refreshTokens.CreateAsync(refresh, user, cancellationToken);
        return response;
    }
    public async Task<AuthTokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByEmailAsync(request.Email.Trim().ToLowerInvariant(), cancellationToken);
        var valid = passwords.VerifyPassword(request.Password, user?.PasswordHash ?? DummyHash);
        if (user is null || !valid) throw new UnauthorizedException("E-mail ou senha inválidos.");
        var (response, refresh) = GenerateTokens(user);
        await refreshTokens.CreateAsync(refresh, cancellationToken: cancellationToken);
        return response;
    }
    public async Task<AuthTokenResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(request.RefreshToken);
        var user = await refreshTokens.GetActiveUserAsync(hash, cancellationToken)
            ?? throw new UnauthorizedException("Refresh token inválido, expirado ou revogado. Entre novamente.");
        var (response, replacement) = GenerateTokens(user);
        if (!await refreshTokens.RotateAsync(hash, replacement, cancellationToken))
            throw new UnauthorizedException("Refresh token inválido, expirado ou revogado. Entre novamente.");
        return response;
    }
    public Task RevokeTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
        => refreshTokens.RevokeAsync(HashToken(request.RefreshToken), cancellationToken);

    private (AuthTokenResponse Response, RefreshToken Token) GenerateTokens(User user)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var now = DateTime.UtcNow;
        var expiresAt = now.AddDays(configuration.GetValue<int>("Jwt:RefreshTokenExpirationDays"));
        var response = tokens.GenerateAccessToken(user) with { RefreshToken = raw, RefreshExpiresAt = expiresAt };
        return (response, new RefreshToken { UserId = user.Id, TokenHash = HashToken(raw), CreatedAt = now, ExpiresAt = expiresAt });
    }
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
