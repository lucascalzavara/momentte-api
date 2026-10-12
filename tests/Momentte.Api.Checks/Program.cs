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
    ["Jwt:AccessTokenExpirationMinutes"] = "60"
}).Build();
var repository = new MemoryUsers();
var passwords = new BCryptPasswordHasher();
var auth = new AuthService(repository, passwords, new JwtTokenService(configuration));
var request = new RegisterRequest { Name = "  Ana Silva  ", Email = "ANA@example.com", Password = "Senha-teste-123" };
var result = await auth.RegisterAsync(request);
Check(repository.Stored?.Name == "Ana Silva" && repository.Stored.Email == "ana@example.com", "Normalização de nome e e-mail");
Check(repository.Stored!.PasswordHash != request.Password && passwords.VerifyPassword(request.Password, repository.Stored.PasswordHash), "Senha protegida por BCrypt");
Check(result.User.Id == repository.Stored.Id && result.ExpiresIn == 3600 && result.TokenType == "Bearer", "Resposta de cadastro");
await Expect<ConflictException>(() => auth.RegisterAsync(request), "E-mail duplicado");
var login = await auth.LoginAsync(new LoginRequest { Email = "ANA@example.com", Password = request.Password });
Check(login.User.Id == result.User.Id, "Login com e-mail normalizado");
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
