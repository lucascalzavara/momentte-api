using Dapper;
using System.Data.Common;
using Npgsql;
using Momentte.Api.Domain.Entities;
using Momentte.Api.Domain.Exceptions;
using Momentte.Api.Infrastructure.Interfaces;
namespace Momentte.Api.Infrastructure.Implementations;
public sealed class RefreshTokenRepository(IDbConnectionFactory factory) : IRefreshTokenRepository
{
    private const string InsertToken = """
        INSERT INTO refresh_tokens (id, user_id, token_hash, created_at, expires_at)
        VALUES (@Id, @UserId, @TokenHash, @CreatedAt, @ExpiresAt);
        """;
    public async Task CreateAsync(RefreshToken token, User? newUser = null, CancellationToken cancellationToken = default)
    {
        using var connection = factory.CreateConnection();
        await ((DbConnection)connection).OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        try
        {
            if (newUser is not null)
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO users (id, name, email, password_hash, created_at, updated_at)
                    VALUES (@Id, @Name, @Email, @PasswordHash, @CreatedAt, @UpdatedAt);
                    """, newUser, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(InsertToken, token, transaction, cancellationToken: cancellationToken));
            transaction.Commit();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation && ex.ConstraintName == "uq_users_email")
        { transaction.Rollback(); throw new ConflictException("Já existe um usuário cadastrado com este e-mail."); }
    }
    public async Task<User?> GetActiveUserAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        using var connection = factory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition("""
            SELECT u.id AS Id, u.name AS Name, u.email AS Email, u.password_hash AS PasswordHash,
                   u.created_at AS CreatedAt, u.updated_at AS UpdatedAt
            FROM refresh_tokens t JOIN users u ON u.id = t.user_id
            WHERE t.token_hash = @TokenHash AND t.revoked_at IS NULL AND t.expires_at > CURRENT_TIMESTAMP;
            """, new { TokenHash = tokenHash }, cancellationToken: cancellationToken));
    }
    public async Task<bool> RotateAsync(string oldTokenHash, RefreshToken replacement, CancellationToken cancellationToken = default)
    {
        using var connection = factory.CreateConnection();
        await ((DbConnection)connection).OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        // O UPDATE condicional adquire lock; apenas uma requisição consegue consumir o token.
        var changed = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE refresh_tokens SET revoked_at = CURRENT_TIMESTAMP
            WHERE token_hash = @TokenHash AND user_id = @UserId
              AND revoked_at IS NULL AND expires_at > CURRENT_TIMESTAMP;
            """, new { TokenHash = oldTokenHash, replacement.UserId }, transaction, cancellationToken: cancellationToken));
        if (changed != 1) { transaction.Rollback(); return false; }
        await connection.ExecuteAsync(new CommandDefinition(InsertToken, replacement, transaction, cancellationToken: cancellationToken));
        transaction.Commit();
        return true;
    }
    public async Task RevokeAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        using var connection = factory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE refresh_tokens SET revoked_at = CURRENT_TIMESTAMP
            WHERE token_hash = @TokenHash AND revoked_at IS NULL;
            """, new { TokenHash = tokenHash }, cancellationToken: cancellationToken));
    }
}
