using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace HrmsBackend.Data;

/// <summary>
/// Equivalent of src/db.js — builds a MySQL connection using the same
/// DB_HOST / DB_USER / DB_PASSWORD / DB_NAME / DB_PORT settings as the
/// original .env file. Connection pooling (waitForConnections / connectionLimit
/// in the Node mysql2 pool) is handled by MySqlConnector's own pool, configured
/// via the connection string below.
/// </summary>
public class DbConnectionFactory
{
    private readonly string _connectionString;

    public DbConnectionFactory(IConfiguration config)
    {
        var host = config["DB_HOST"] ?? "localhost";
        var user = config["DB_USER"] ?? "root";
        var password = config["DB_PASSWORD"] ?? "";
        var database = config["DB_NAME"] ?? "hrms_db";
        var port = config["DB_PORT"] ?? "3306";

        // TreatTinyAsBoolean=false keeps TINYINT(1) columns (is_active, is_default,
        // can_view, ...) coming back as 0/1 numbers, matching mysql2's default
        // behavior in the original Node app, instead of C#/.NET's usual true/false.
        _connectionString =
            $"Server={host};Port={port};Database={database};Uid={user};Pwd={password};" +
            "TreatTinyAsBoolean=false;Pooling=true;Maximum Pool Size=10;";
    }

    public MySqlConnection CreateConnection() => new(_connectionString);
}
