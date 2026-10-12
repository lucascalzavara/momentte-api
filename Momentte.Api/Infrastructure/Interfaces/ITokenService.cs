using Momentte.Api.Domain.Entities;
using Momentte.Api.Domain.DTOs.Auth;
namespace Momentte.Api.Infrastructure.Interfaces;
public interface ITokenService { AuthTokenResponse GenerateAccessToken(User user); }
