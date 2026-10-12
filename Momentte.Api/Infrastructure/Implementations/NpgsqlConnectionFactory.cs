using System.Data;
using Npgsql;
using Momentte.Api.Infrastructure.Interfaces;
namespace Momentte.Api.Infrastructure.Implementations;
public sealed class NpgsqlConnectionFactory(IConfiguration configuration) : IDbConnectionFactory
{
    public IDbConnection CreateConnection() => new NpgsqlConnection(
        configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection."));
}
