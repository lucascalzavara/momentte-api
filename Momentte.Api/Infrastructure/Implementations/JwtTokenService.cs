using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Momentte.Api.Domain.DTOs.Auth;
using Momentte.Api.Domain.Entities;
using Momentte.Api.Infrastructure.Interfaces;
namespace Momentte.Api.Infrastructure.Implementations;
public sealed class JwtTokenService(IConfiguration configuration) : ITokenService
{
    public AuthTokenResponse GenerateAccessToken(User user)
    {
        var now = DateTime.UtcNow;
        var expiresIn = configuration.GetValue<int>("Jwt:AccessTokenExpirationMinutes") * 60;
        var expiresAt = now.AddSeconds(expiresIn);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:SecretKey"]!));
        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"], audience: configuration["Jwt:Audience"],
            claims: [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                     new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                     new Claim(JwtRegisteredClaimNames.Email, user.Email), new Claim("name", user.Name)],
            notBefore: now, expires: expiresAt, signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new AuthTokenResponse(new JwtSecurityTokenHandler().WriteToken(token), "Bearer", expiresIn, expiresAt,
            new UserInfoDto(user.Id, user.Name, user.Email));
    }
}
