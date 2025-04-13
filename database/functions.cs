using MySqlConnector;

namespace ServersList;

public partial class ServersList
{
    public static async Task<MySqlConnection> ConnectAsync()
    {
        MySqlConnection connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
