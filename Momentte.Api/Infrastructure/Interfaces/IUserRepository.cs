using Momentte.Api.Domain.Entities;
namespace Momentte.Api.Infrastructure.Interfaces;
public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task CreateAsync(User user, CancellationToken cancellationToken = default);
}
