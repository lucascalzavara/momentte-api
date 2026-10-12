using System.Data;
namespace Momentte.Api.Infrastructure.Interfaces;
public interface IDbConnectionFactory { IDbConnection CreateConnection(); }
