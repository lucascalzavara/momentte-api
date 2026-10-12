using Dapper;
using Npgsql;
using Momentte.Api.Domain.Entities;
using Momentte.Api.Domain.Exceptions;
using Momentte.Api.Infrastructure.Interfaces;
namespace Momentte.Api.Infrastructure.Implementations;
public sealed class UserRepository(IDbConnectionFactory connectionFactory) : IUserRepository
{
    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id AS Id, name AS Name, email AS Email, password_hash AS PasswordHash,
                   created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM users WHERE lower(email) = @Email;
            """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition(sql, new { Email = email }, cancellationToken: cancellationToken));
    }
    public async Task CreateAsync(User user, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO users (id, name, email, password_hash, created_at, updated_at)
            VALUES (@Id, @Name, @Email, @PasswordHash, @CreatedAt, @UpdatedAt);
            """;
        using var connection = connectionFactory.CreateConnection();
        try { await connection.ExecuteAsync(new CommandDefinition(sql, user, cancellationToken: cancellationToken)); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation && ex.ConstraintName == "uq_users_email")
        { throw new ConflictException("Já existe um usuário cadastrado com este e-mail."); }
    }
}
