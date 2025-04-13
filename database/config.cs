using CounterStrikeSharp.API;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Newtonsoft.Json;

namespace ServersList;

public partial class ServersList
{
    public int serverIdentifier = 0;
    public static string? ConnectionString { get; set; } = string.Empty;

    private class DatabaseConfig
    {
        public string? Server { get; set; }
        public uint Port { get; set; }
        public string? UserID { get; set; }
        public string? Password { get; set; }
        public string? Database { get; set; }
    }

    private class serverID { 
        public uint id { get; set; }
    }

    private async Task LoadDatabaseCredentialsAsync()
    {
        string configFilePath = Config.DatabaseCredentials;
        if (!File.Exists(configFilePath))
        {
            Logger.LogCritical("Database configuration file not found.");
            serverIdentifier = -1;
            return;
        }
        try
        {
            string json = await File.ReadAllTextAsync(configFilePath);
            var config = JsonConvert.DeserializeObject<DatabaseConfig>(json);

            if (config != null)
            {
                MySqlConnectionStringBuilder builder =
                    new()
                    {
                        Server = config.Server,
                        Database = config.Database,
                        UserID = config.UserID,
                        Password = config.Password,
                        Port = config.Port,
                        Pooling = true,
                        MinimumPoolSize = 0,
                        MaximumPoolSize = 640,
                        ConnectionIdleTimeout = 30,
                        AllowZeroDateTime = true
                    };
                ConnectionString = builder.ConnectionString;
                Logger.LogInformation("Loaded database configuration");
            }
            else
            {
                Logger.LogCritical("Failed to parse database configuration file.");
                serverIdentifier = -1;
            }
        }
        catch (Exception ex)
        {
            Logger.LogCritical($"Error loading database configuration: {ex.Message}");
            serverIdentifier = -1;
        }
    }

    private async Task LoadServerIDAsync()
    {
        string idFilePath = Config.ServerIdFile;
        if (serverIdentifier == -1) return;
        if (!File.Exists(idFilePath))
        {
            Logger.LogCritical("Server ID configuration file not found.");
            serverIdentifier = -1;
            return;
        }
        try
        {
            string json = await File.ReadAllTextAsync(idFilePath);
            var config = JsonConvert.DeserializeObject<serverID>(json);

            if (config != null)
            {
                serverIdentifier = (int)config.id;
            }
            else
            {
                serverIdentifier = -1;
                Logger.LogCritical("Failed to parse server ID file.");
            }
        }
        catch (Exception ex)
        {
            serverIdentifier = -1;
            Logger.LogCritical($"Error loading Server ID: {ex.Message}");
        }
    }

    private async Task CheckAndPrepare() {
        string mysqlQuery = $"SELECT * FROM `{Config.TableName}` WHERE `id` = '{serverIdentifier}';";
        string mysqlCreateTableQuery = $"CREATE TABLE IF NOT EXISTS {Config.TableName} (id INT PRIMARY KEY, ip  VARCHAR(64), name VARCHAR(64), map_name VARCHAR(64) DEFAULT 'null', active_players INT DEFAULT -1, max_players INT DEFAULT 0, max_players_offset INT DEFAULT 0);";
        if (serverIdentifier <= 0)
        {
            Logger.LogCritical("There were some errors/serverIdentifier not set. Plugin won't work.");
            return;
        }
        else
        {
            try
            {
                using var _connection = await ConnectAsync();
                using var querry = new MySqlCommand(mysqlQuery, _connection);
                using var result = await querry.ExecuteReaderAsync();
                if (await result.ReadAsync())
                {
                    if (result.GetString("ip").Length < 3 || result.GetString("name").Length < 3) {
                        Server.NextFrame(() =>
                        {
                            Logger?.LogCritical("This server don't have name/ip set in the database. Plugin might not work correctly.");
                        });
                    }
                }
                else {
                    Server.NextFrame(() =>   
                    {
                        Logger?.LogCritical("This server don't exist in the database. Plugin won't work.");
                    });
                }
                await _connection.CloseAsync();
            }
            catch (MySqlException ex)
            {
                if (ex.ErrorCode == MySqlErrorCode.NoSuchTable)
                {
                    try
                    {
                        using var _connectionCreate = await ConnectAsync();
                        using var querry = new MySqlCommand(mysqlCreateTableQuery, _connectionCreate);
                        await querry.ExecuteNonQueryAsync();
                        Server.NextFrame(() =>
                        {
                            Logger.LogInformation($"Table not found in database, creating one.");
                            Logger.LogInformation($"You need to insert data about servers into database for plugin to work. To add this server use command: (css_/!)serverslist edit");
                        });
                        await _connectionCreate.CloseAsync();
                    }
                    catch (MySqlException exCreate)
                    {
                        serverIdentifier = -1;
                        Server.NextFrame(() =>
                        {
                            Logger?.LogError($"Could not create instance of MySqlConnection, error: '{exCreate.Message}'. Operation will not happen: 'OnConfigParsedCreateTableIfNotExist'");
                        });
                    }
                }
                else
                {
                    serverIdentifier = -1;
                    Server.NextFrame(() =>
                    {
                        Logger?.LogError($"Could not create instance of MySqlConnection, error: '{ex.Message}'. Operation will not happen: 'OnConfigParsed'");
                    });
                }
            }
        }
    }
}
