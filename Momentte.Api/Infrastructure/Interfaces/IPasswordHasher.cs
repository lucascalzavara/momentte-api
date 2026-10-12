namespace Momentte.Api.Infrastructure.Interfaces;
public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string passwordHash);
}
