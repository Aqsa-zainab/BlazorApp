using Microsoft.Data.SqlClient;
using System.Data;

namespace BlazorWebApp.Services;

// 1. The Interface must be public
public interface ISqlConnectionFactory
{
    IDbConnection CreateConnection();
}

// 2. The Class MUST inherit the interface using the colon (:)
public class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string is missing.");
    }

    public IDbConnection CreateConnection()
    {
        return new SqlConnection(_connectionString);
    }
}