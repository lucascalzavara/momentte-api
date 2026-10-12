namespace Momentte.Api.Domain.DTOs.Auth;

public sealed record UserInfoDto(Guid Id, string Name, string Email);
public sealed record AuthTokenResponse(string AccessToken, string TokenType, int ExpiresIn, DateTime ExpiresAt, UserInfoDto User)
{
    public string RefreshToken { get; init; } = string.Empty;
    public DateTime RefreshExpiresAt { get; init; }
}
