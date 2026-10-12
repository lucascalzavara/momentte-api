using Momentte.Api.Application.Interfaces;
using Momentte.Api.Domain.DTOs.Auth;
using Momentte.Api.Domain.Entities;
using Momentte.Api.Domain.Exceptions;
using Momentte.Api.Infrastructure.Interfaces;
namespace Momentte.Api.Application.Implementations;
public sealed class AuthService(IUserRepository users, IPasswordHasher passwords, ITokenService tokens) : IAuthService
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
        var response = tokens.GenerateAccessToken(user);
        await users.CreateAsync(user, cancellationToken);
        return response;
    }
    public async Task<AuthTokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByEmailAsync(request.Email.Trim().ToLowerInvariant(), cancellationToken);
        var valid = passwords.VerifyPassword(request.Password, user?.PasswordHash ?? DummyHash);
        if (user is null || !valid) throw new UnauthorizedException("E-mail ou senha inválidos.");
        return tokens.GenerateAccessToken(user);
    }
}
