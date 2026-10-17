using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Momentte.Api.Application.Implementations;
using Momentte.Api.Domain.DTOs.Auth;
using Momentte.Api.Domain.Entities;
using Momentte.Api.Domain.Exceptions;
using Momentte.Api.Infrastructure.Implementations;
using Momentte.Api.Infrastructure.Interfaces;

var secret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Jwt:SecretKey"] = secret, ["Jwt:Issuer"] = "Momentte.Api", ["Jwt:Audience"] = "Momentte.Web",
    ["Jwt:AccessTokenExpirationMinutes"] = "60", ["Jwt:RefreshTokenExpirationDays"] = "15"
}).Build();
var repository = new MemoryUsers();
var passwords = new BCryptPasswordHasher();
var refreshRepository = new MemoryRefreshTokens(repository);
var auth = new AuthService(repository, passwords, new JwtTokenService(configuration), refreshRepository, configuration);
var request = new RegisterRequest { Name = "  Ana Silva  ", Email = "ANA@example.com", Password = "Senha-teste-123" };
var result = await auth.RegisterAsync(request);
Check(repository.Stored?.Name == "Ana Silva" && repository.Stored.Email == "ana@example.com", "Normalização de nome e e-mail");
Check(repository.Stored!.PasswordHash != request.Password && passwords.VerifyPassword(request.Password, repository.Stored.PasswordHash), "Senha protegida por BCrypt");
Check(result.User.Id == repository.Stored.Id && result.ExpiresIn == 3600 && result.TokenType == "Bearer", "Resposta de cadastro");
await Expect<ConflictException>(() => auth.RegisterAsync(request), "E-mail duplicado");
var login = await auth.LoginAsync(new LoginRequest { Email = "ANA@example.com", Password = request.Password });
Check(login.User.Id == result.User.Id, "Login com e-mail normalizado");
Check(login.RefreshToken != result.RefreshToken && login.RefreshExpiresAt > DateTime.UtcNow.AddDays(14), "Refresh independente por login, válido por 15 dias");
Check(refreshRepository.Tokens.All(t => t.TokenHash != login.RefreshToken && t.TokenHash.Length == 64), "Somente hash SHA-256 armazenado");
var renewed = await auth.RefreshTokenAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken });
Check(renewed.RefreshToken != login.RefreshToken && renewed.User.Id == login.User.Id, "Rotação preserva usuário e troca refresh token");
await Expect<UnauthorizedException>(() => auth.RefreshTokenAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken }), "Token consumido não pode ser reutilizado");
await auth.RevokeTokenAsync(new RefreshTokenRequest { RefreshToken = renewed.RefreshToken });
await auth.RevokeTokenAsync(new RefreshTokenRequest { RefreshToken = renewed.RefreshToken });
await Expect<UnauthorizedException>(() => auth.RefreshTokenAsync(new RefreshTokenRequest { RefreshToken = renewed.RefreshToken }), "Logout revoga refresh, com revogação idempotente");
await Expect<UnauthorizedException>(() => auth.RefreshTokenAsync(new RefreshTokenRequest { RefreshToken = "invalido" }), "Refresh desconhecido rejeitado");
refreshRepository.Expire(result.RefreshToken);
await Expect<UnauthorizedException>(() => auth.RefreshTokenAsync(new RefreshTokenRequest { RefreshToken = result.RefreshToken }), "Refresh expirado rejeitado");
var concurrentLogin = await auth.LoginAsync(new LoginRequest { Email = request.Email, Password = request.Password });
var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
{
    try { await auth.RefreshTokenAsync(new RefreshTokenRequest { RefreshToken = concurrentLogin.RefreshToken }); return true; }
    catch (UnauthorizedException) { return false; }
}));
Check(attempts.Count(success => success) == 1, "Somente uma rotação por token");
await Expect<UnauthorizedException>(() => auth.LoginAsync(new LoginRequest { Email = request.Email, Password = "senha errada" }), "Senha incorreta");
await Expect<UnauthorizedException>(() => auth.LoginAsync(new LoginRequest { Email = "outro@example.com", Password = request.Password }), "Usuário inexistente");
var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
var principal = handler.ValidateToken(login.AccessToken, new TokenValidationParameters
{
    ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
    ValidIssuer = "Momentte.Api", ValidAudience = "Momentte.Web", ClockSkew = TimeSpan.Zero,
    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
}, out _);
Check(principal.FindFirst("sub")?.Value == result.User.Id.ToString(), "Assinatura, validade e identidade do JWT");
foreach (var invalid in new[] {
    new RegisterRequest { Name = " ", Email = "ana@example.com", Password = "Senha123" },
    new RegisterRequest { Name = "Ana", Email = "invalido", Password = "Senha123" },
    new RegisterRequest { Name = "Ana", Email = "ana@example.com", Password = "curta" },
    new RegisterRequest { Name = "Ana", Email = "ana@example.com", Password = new string('á', 37) }
})
    Check(!Validator.TryValidateObject(invalid, new ValidationContext(invalid), [], true), "Rejeição de cadastro inválido");
await BillingChecks.Run();
EventChecks.Run();
await ArchiveChecks.Run();
Console.WriteLine("Todas as verificações passaram. Repositório em memória; não acessa PostgreSQL.");

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"Falhou: {name}");
    Console.WriteLine($"OK: {name}");
}
static async Task Expect<T>(Func<Task<AuthTokenResponse>> action, string name) where T : Exception
{
    try { await action(); } catch (T) { Console.WriteLine($"OK: {name}"); return; }
    throw new Exception($"Falhou: {name}");
}
sealed class MemoryUsers : IUserRepository
{
    public User? Stored { get; private set; }
    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        => Task.FromResult(Stored?.Email == email ? Stored : null);
    public Task CreateAsync(User user, CancellationToken cancellationToken = default)
    { Stored = user; return Task.CompletedTask; }
}
sealed class MemoryRefreshTokens(MemoryUsers users) : IRefreshTokenRepository
{
    private readonly Dictionary<string, RefreshToken> tokens = [];
    private readonly HashSet<string> revoked = [];
    public IEnumerable<RefreshToken> Tokens => tokens.Values;
    private static string Hash(string raw) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    public void Expire(string raw)
    {
        var hash = Hash(raw); var old = tokens[hash];
        tokens[hash] = new RefreshToken { Id = old.Id, UserId = old.UserId, TokenHash = old.TokenHash, CreatedAt = old.CreatedAt, ExpiresAt = DateTime.UtcNow.AddDays(-1) };
    }
    public async Task CreateAsync(RefreshToken token, User? newUser = null, CancellationToken cancellationToken = default)
    {
        if (newUser is not null) await users.CreateAsync(newUser, cancellationToken);
        tokens.Add(token.TokenHash, token);
    }
    public Task<User?> GetActiveUserAsync(string tokenHash, CancellationToken cancellationToken = default)
        => Task.FromResult(tokens.TryGetValue(tokenHash, out var token) && !revoked.Contains(tokenHash) && token.ExpiresAt > DateTime.UtcNow ? users.Stored : null);
    public Task<bool> RotateAsync(string oldTokenHash, RefreshToken replacement, CancellationToken cancellationToken = default)
    {
        lock (tokens)
        {
            if (!tokens.TryGetValue(oldTokenHash, out var old) || revoked.Contains(oldTokenHash) || old.ExpiresAt <= DateTime.UtcNow || old.UserId != replacement.UserId)
                return Task.FromResult(false);
            revoked.Add(oldTokenHash); tokens.Add(replacement.TokenHash, replacement);
            return Task.FromResult(true);
        }
    }
    public Task RevokeAsync(string tokenHash, CancellationToken cancellationToken = default)
    { revoked.Add(tokenHash); return Task.CompletedTask; }
}
