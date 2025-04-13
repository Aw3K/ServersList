using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Config;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ServersList;

public class ServerInstance {
    public required int id { get; set; }
    public required string ip { get; set; }
    public required string name { get; set; }
    public required int teamCount { get; set; }
    public required int maxPlayers { get; set; }
    public required int maxPlayersOffset { get; set; }
    public required string mapName { get; set; }
    [SetsRequiredMembers]
    public ServerInstance(int id, string ip, string name, int teamCount, int maxPlayers, int maxPlayersOffset, string mapName) =>
    (this.id, this.ip, this.name, this.teamCount,this.maxPlayers, this.maxPlayersOffset, this.mapName) = (id, ip, name, teamCount, maxPlayers, maxPlayersOffset, mapName);
}

public class ServersListConfig : BasePluginConfig
{
    [JsonPropertyName("DatabaseCredentials")] public string DatabaseCredentials { get; set; } = "/home/container/game/csgo/addons/counterstrikesharp/configs/database.json";
    [JsonPropertyName("ServerIdFile")] public string ServerIdFile { get; set; } = "/home/container/game/csgo/addons/counterstrikesharp/configs/id.json";
    [JsonPropertyName("TableName")] public string TableName { get; set; } = "serverslist_servers";
    [JsonPropertyName("BasicPermissions")] public string BasicPermissions { get; set; } = "@css/ban";
    [JsonPropertyName("RootPermissions")] public string RootPermissions { get; set; } = "@css/root";
}

public partial class ServersList : BasePlugin, IPluginConfig<ServersListConfig>
{
    public override string ModuleName => "ServersList";
    public override string ModuleAuthor => "NyggaBytes";
    public override string ModuleVersion => "1.1.3";
    public override string ModuleDescription => "";
    public ServersListConfig Config { get; set; } = new();

    private List<ServerInstance>? Servers = new List<ServerInstance>();

    public override void Load(bool hotReload)
    {
        Logger.LogInformation($"Plugin version: {ModuleVersion}");
        Task.Run(async () => {
            await LoadDatabaseCredentialsAsync();
            await LoadServerIDAsync();
            await CheckAndPrepare();
        }).Wait();
        if (serverIdentifier > 0) Logger.LogInformation($"Loaded plugin, hooked to id: {serverIdentifier}");

        loadServers();
        RegisterListener<Listeners.OnServerPreFatalShutdown>(setShutdownInDataBase);
        RegisterListener<Listeners.OnMapStart>(setPlayerCountAndMapStartup);
    }

    #region Commands
    [ConsoleCommand("css_serverslist", "ServersList command for admins to manage.")]
    [CommandHelper(minArgs: 1, usage: "Basic: INFO|RSERVERS|HELP Root: LIST|EDIT|DELETE|OFFSET|RELOAD", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnServersListCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!AdminManager.PlayerHasPermissions(player, Config.BasicPermissions))
        {
            command.ReplyToCommand(string.Format(Localizer["NoPerms"], Config.BasicPermissions));
            return;
        }

        if (command.ArgString == "INFO" || command.ArgString == "info")
        {
            command.ReplyToCommand($"[\u0004ServersList\u0001] Information");
            command.ReplyToCommand($" \u0004Plugin Version\u0001: " + ModuleVersion);
            command.ReplyToCommand($" \u0004ServerIdentifier\u0001: {serverIdentifier}");
            command.ReplyToCommand($" \u0004Servers loaded\u0001: {Servers!.Count()}");
            try
            {
                using var _connection = new MySqlConnection(ConnectionString);
                _connection.Open();
                command.ReplyToCommand($" \u0004Database connection\u0001: {_connection.State.ToString()}");
                _connection.Close();
            }
            catch (MySqlException e)
            {
                command.ReplyToCommand($" \u0004Database connection\u0001: {e.Message}");
            }
            command.ReplyToCommand($" \u0004Basic Permissions\u0001: {Config.BasicPermissions}");
            command.ReplyToCommand($" \u0004Root Permissions\u0001: {Config.RootPermissions}");
            command.ReplyToCommand($"[/\u0004ServersList\u0001]");
            return;
        }

        if (command.ArgString == "RSERVERS" || command.ArgString == "rservers")
        {
            loadServers();
            command.ReplyToCommand($" \u0004Scheduled task for servers reload.");
            return;
        }

        if (command.ArgString == "HELP" || command.ArgString == "help")
        {
            command.ReplyToCommand($"[\u0004ServersList\u0001] Help");
            command.ReplyToCommand($" (css_/!)serverslist <OPTION> <ARGUMENTS>");
            command.ReplyToCommand($" \u0004BASIC OPTIONS\u0001:");
            command.ReplyToCommand($" \u0004HELP \u0001- displays this information");
            command.ReplyToCommand($" \u0004INFO \u0001- displays information about plugin such as version/database/permissions");
            command.ReplyToCommand($" \u0004RSERVERS \u0001- run async task for servers reload from database");
            command.ReplyToCommand($" \u0004ROOT OPTIONS\u0001:");
            command.ReplyToCommand($" \u0004LIST \u0001- gathers and displays raw data about servers from database");
            command.ReplyToCommand($" \u0004EDIT <\"IP OF SERVER\"> <\"NAME OF SERVER\"> \u0001- updates ip and name of current server or inserts row into database if not exist, name and ip must be inside \"\"");
            command.ReplyToCommand($" \u0004DELETE <SERVER ID> \u0001- deletes record of server from database with a given id");
            command.ReplyToCommand($" \u0004OFFSET <SERVER ID> <NEW VALUE> \u0001- change value of max_players_offset in database at given server id");
            command.ReplyToCommand($" \u0004RELOAD \u0001- reloads plugin configuration, hooks again to server id");
            command.ReplyToCommand($"[/\u0004ServersList\u0001]");
            return;
        }

        if (!AdminManager.PlayerHasPermissions(player, Config.RootPermissions))
        {
            command.ReplyToCommand(string.Format(Localizer["NoPerms"], Config.RootPermissions));
            return;
        }
        else
        {
            if (command.ArgByIndex(1) == "RELOAD" || command.ArgByIndex(1) == "reload")
            {
                OnConfigParsed(ConfigManager.Load<ServersListConfig>("ServersList"));
                Task.Run(async () => {
                    await LoadDatabaseCredentialsAsync();
                    await LoadServerIDAsync();
                    await CheckAndPrepare();
                }).Wait();
                command.ReplyToCommand($"[\u0004ServersList\u0001] Reloading");
                if (serverIdentifier > 0) {
                    command.ReplyToCommand($" \u0004Reloaded plugin, hooked to id: \u0007{serverIdentifier}");
                    loadServers();
                }
                else command.ReplyToCommand($" \u0004Reloaded plugin, but \u0007couldn't hook to any id\u0004, check configuration and if specified server is present in database, to add this server use command: (css_/!)serverslist edit");
                command.ReplyToCommand($"[/\u0004ServersList\u0001]");
                return;
            }

            if (command.ArgByIndex(1) == "EDIT" || command.ArgByIndex(1) == "edit")
            {
                if (command.ArgCount != 4) { command.ReplyToCommand($" \u0004usage\u0001: (css_/!)serverslist edit \"IP OF SERVER\" \"NAME OF SERVER\""); return; }
                else if (command.ArgCount == 4)
                {
                    var ip = command.ArgByIndex(2);
                    var name = command.ArgByIndex(3);
                    if (ip.Length < 1 || name.Length < 1) {
                        command.ReplyToCommand(" \u0007Too short name/ip.");
                        return;
                    }
                    if (serverIdentifier <= 0) {
                        command.ReplyToCommand(" \u0007Can't edit/insert server data, serverIdentifier is not hooked to any server.");
                        return;
                    }
                    try
                    {
                        using var _connection = new MySqlConnection(ConnectionString);
                        _connection.Open();
                        using var querryUpdate = new MySqlCommand($"INSERT INTO `{Config.TableName}`(`id`, `ip`, `name`) VALUES (@id,@ip,@name) ON DUPLICATE KEY UPDATE `ip`=VALUES(`ip`),`name`=VALUES(`name`);", _connection);
                        querryUpdate.Parameters.AddWithValue("id", serverIdentifier);
                        querryUpdate.Parameters.AddWithValue("ip", command.ArgByIndex(2));
                        querryUpdate.Parameters.AddWithValue("name", command.ArgByIndex(3));
                        var resultUpdate = querryUpdate.ExecuteNonQuery();
                        if (resultUpdate != 1) command.ReplyToCommand($" \u0007Error in function ServersListCommandEditUpdate, {resultUpdate} rows affected instead of 1.");
                        else if (resultUpdate == 1) command.ReplyToCommand($" \u0004Successfully updated ip and name of current server.");
                        _connection.Close();
                    }
                    catch (MySqlException exUpdate)
                    {
                        command.ReplyToCommand($"Could not create instance of MySqlConnection, error: '\u0007{exUpdate.Message}\u0001'. Operation will not happen: 'ServersListCommandEditUpdate'");
                    }
                }
                return;
            }

            if (command.ArgByIndex(1) == "DELETE" || command.ArgByIndex(1) == "delete")
            {
                if (command.ArgCount != 3) { command.ReplyToCommand($" \u0004usage\u0001: (css_/!)serverslist delete <SERVER ID>"); return; }
                else if (command.ArgCount == 3)
                {
                    try
                    {
                        using var _connection = new MySqlConnection(ConnectionString);
                        _connection.Open();
                        using var querry = new MySqlCommand($"SELECT `id` FROM `{Config.TableName}` WHERE `id` = @id;", _connection);
                        querry.Parameters.AddWithValue("id", command.ArgByIndex(2));
                        using var result = querry.ExecuteReader();
                        if (result.Read())
                        {
                            try
                            {
                                using var _connectionDelete = new MySqlConnection(ConnectionString);
                                _connectionDelete.Open();
                                using var querryDelete = new MySqlCommand($"DELETE FROM `{Config.TableName}` WHERE `id` = @id;", _connectionDelete);
                                querryDelete.Parameters.AddWithValue("id", command.ArgByIndex(2));
                                var resultDelete = querryDelete.ExecuteNonQuery();
                                if (resultDelete != 1) command.ReplyToCommand($"Error in operation 'ServersListCommandDeleteOperation', {resultDelete} rows affected instead of 1.");
                                else command.ReplyToCommand(" \u0004Successfully deleted record with given id from database.");
                                _connectionDelete.Close();
                            }
                            catch (MySqlException exDelete)
                            {
                                command.ReplyToCommand($"Could not create instance of MySqlConnection, error: '\u0007{exDelete.Message}\u0001'. Operation will not happen: 'ServersListCommandDeleteOperation'");
                            }
                        }
                        else command.ReplyToCommand($" \u0007Server with given id don't exist in database.");
                        _connection.Close();
                    }
                    catch (MySqlException ex)
                    {
                        command.ReplyToCommand($"Could not create instance of MySqlConnection, error: '\u0007{ex.Message}\u0001'. Operation will not happen: 'ServersListCommandDeleteCheckExist'");
                    }
                }
                return;
            }

            if (command.ArgByIndex(1) == "LIST" || command.ArgByIndex(1) == "list")
            {
                string mysqlQuery = $"SELECT `id`,`ip`,`name`,`active_players`,`max_players`,`max_players_offset`,`map_name` FROM `{Config.TableName}`;";
                Task.Run(async () => {
                    Server.NextFrame(() => {
                        command.ReplyToCommand($"[\u0004ServersList\u0001] List");
                        command.ReplyToCommand("\u0004|> \u0001id \u0004| \u0001ip \u0004| \u0001name \u0004| \u0001active_players \u0004| \u0001max_players \u0004| \u0001max_players_offset \u0004| \u0001map_name");
                    });
                    try
                    {
                        using var _connection = await ConnectAsync();
                        using var querry = new MySqlCommand(mysqlQuery, _connection);
                        await using var result = await querry.ExecuteReaderAsync();
                        while (await result.ReadAsync())
                        {
                            var output = $"\u0004|> \u0001{result.GetInt32(0)} \u0004| \u0001{result.GetString(1)} \u0004| \u0001{result.GetString(2)} \u0004| \u0001{result.GetInt32(3)} \u0004| \u0001{result.GetInt32(4)} \u0004| \u0001{result.GetInt32(5)} \u0004| \u0001{result.GetString(6)}";
                            Server.NextFrame(() => {
                                command.ReplyToCommand(output);
                            });
                        }
                        await _connection.CloseAsync();
                    }
                    catch (MySqlException ex)
                    {
                        Server.NextFrame(() => {
                            command.ReplyToCommand($"Could not create instance of MySqlConnection, error: '\u0007{ex.Message}\u0001'. Operation will not happen: 'ServersListCommandList'");
                        });
                    }
                    Server.NextFrame(() => {
                        command.ReplyToCommand($"[/\u0004ServersList\u0001]");
                    });
                });
                return;
            }

            if (command.ArgByIndex(1) == "OFFSET" || command.ArgByIndex(1) == "offset")
            {
                if (command.ArgCount != 4) { command.ReplyToCommand($" \u0004usage\u0001: (css_/!)serverslist offset <SERVER ID> <NEW VALUE>"); return; }
                else if (command.ArgCount == 4)
                {
                    try
                    {
                        using var _connection = new MySqlConnection(ConnectionString);
                        _connection.Open();
                        using var querry = new MySqlCommand($"SELECT `id` FROM `{Config.TableName}` WHERE `id` = @id;", _connection);
                        querry.Parameters.AddWithValue("id", command.ArgByIndex(2));
                        using var result = querry.ExecuteReader();
                        if (result.Read())
                        {
                            try
                            {
                                if (int.TryParse(command.ArgByIndex(3), out int offset))
                                {
                                    using var _connectionSet = new MySqlConnection(ConnectionString);
                                    _connectionSet.Open();
                                    using var querrySet = new MySqlCommand($"UPDATE `{Config.TableName}` SET `max_players_offset` = @offset WHERE `id` = @id;", _connectionSet);
                                    querrySet.Parameters.AddWithValue("offset", offset);
                                    querrySet.Parameters.AddWithValue("id", command.ArgByIndex(2));
                                    var resultSet = querrySet.ExecuteNonQuery();
                                    if (resultSet != 1) command.ReplyToCommand($"Error in operation 'ServersListCommandOffsetSet', {resultSet} rows affected instead of 1.");
                                    else command.ReplyToCommand(" \u0004Successfully set offset with given id in database.");
                                    _connectionSet.Close();
                                }
                                else command.ReplyToCommand($" \u0007Given offset is not a valid number.");
                            } catch (MySqlException exChange)
                            {
                                command.ReplyToCommand($"Could not create instance of MySqlConnection, error: '\u0007{exChange.Message}\u0001'. Operation will not happen: 'ServersListCommandOffsetSet'");
                            }
                        }
                        else command.ReplyToCommand($" \u0007Server with given id don't exist in database.");
                        _connection.Close();
                    } catch (MySqlException ex)
                    {
                        command.ReplyToCommand($"Could not create instance of MySqlConnection, error: '\u0007{ex.Message}\u0001'. Operation will not happen: 'ServersListCommandOffsetCheckExist'");
                    }
                }
            }
        }
    }

    [ConsoleCommand("css_servers", "Servers list.")]
    [ConsoleCommand("css_serwery", "Lista serwerów.")]
    [CommandHelper(minArgs: 0, usage: "<NAME>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnServersCommand(CCSPlayerController? player, CommandInfo command)
    {
        command.ReplyToCommand(Localizer["LOGO"]);
        if (Servers?.Count() == 0 || serverIdentifier == -1)
        {
            command.ReplyToCommand(Localizer["DatabaseError"]);
            return;
        }
        if (command.ArgString != "")
        {
            List<ServerInstance> founded = Servers!.FindAll(
                delegate(ServerInstance e) { return e.name.ToLower().Contains(command.ArgString.ToLower()) || e.mapName.ToLower().Contains(command.ArgString.ToLower()); }
            );
            if (founded.Count() < 1) command.ReplyToCommand(Localizer["NotFoundAny"]);
            else if (founded.Count() == 1)
            {
                if (founded.First().teamCount == -1) command.ReplyToCommand(string.Format(Localizer["OFFLINE"], founded.First().name));
                else
                {
                    if (founded.First().id != serverIdentifier)
                    {
                        command.ReplyToCommand(string.Format(Localizer["ConnectWithFL"], founded.First().name, founded.First().teamCount, founded.First().maxPlayers + founded.First().maxPlayersOffset, founded.First().mapName));
                        command.ReplyToCommand(string.Format(Localizer["ConnectWithSL"], founded.First().ip));
                    }
                    else command.ReplyToCommand(string.Format(Localizer["InfoOwn"], founded.First().ip)); 
                }
            }
            else foreach(var server in founded) replyToCommandList(command, server.id, server.ip, server.name, server.teamCount, server.maxPlayers+server.maxPlayersOffset, server.mapName);
        } else foreach (var server in Servers!) replyToCommandList(command, server.id, server.ip, server.name, server.teamCount, server.maxPlayers+server.maxPlayersOffset, server.mapName);
        command.ReplyToCommand(" ");
    }
    #endregion

    #region Events
    [GameEventHandler(HookMode.Pre)]
    public HookResult OnServerShutdown(EventServerShutdown @event, GameEventInfo @info) {
        setShutdownInDataBase();
        return HookResult.Continue;
    }
    [GameEventHandler(HookMode.Post)]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo @info)
    {
        loadServers();
        setPlayerCount(ActivePlayersCount());
        return HookResult.Continue;
    }
    [GameEventHandler(HookMode.Post)]
    public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo @info)
    {
        loadServers();
        setPlayerCount(ActivePlayersCount());
        return HookResult.Continue;
    }
    [GameEventHandler(HookMode.Post)]
    public HookResult OnRoundPrestart(EventRoundStart @event, GameEventInfo info)
    {
        loadServers();
        setPlayerCount(ActivePlayersCount());
        return HookResult.Continue;
    }
    #endregion

    #region functions
    public void replyToCommandList(CommandInfo command, int id, string ip, string name, int playerCount, int maxPlayers, string mapName) {
        if (playerCount == -1) command.ReplyToCommand(string.Format(Localizer["OFFLINE"], name));
        else if (playerCount == -2) command.ReplyToCommand(string.Format(Localizer["DatabaseErrList"], name));
        else {
            if (id == serverIdentifier) command.ReplyToCommand(string.Format(Localizer["MultipleOutFL"], name, ip));
            else command.ReplyToCommand(string.Format(Localizer["MultipleOutSL"], name, mapName, playerCount, maxPlayers, ip));
        }
    }
    private void setPlayerCountAndMapStartup(string mapName) {
        string mysqlQuery = $"UPDATE `{Config.TableName}` SET `active_players` = '0', `map_name` = '{mapName}', `max_players` = '{Server.MaxPlayers}' WHERE `id` = '{serverIdentifier}';";
        Task.Run(async () => {
            try
            {
                using var _connection = new MySqlConnection(ConnectionString);
                await _connection.OpenAsync();
                await using var querry = new MySqlCommand(mysqlQuery, _connection);
                var result = await querry.ExecuteNonQueryAsync();
                if (result != 1) Logger?.LogError($"Error in function setPlayerCountAndMapStartu, {result} rows affected instead of 1.");
                await _connection.CloseAsync();
            }
            catch (MySqlException ex)
            {
                Logger?.LogError($"Could not create instance of MySqlConnection, error: '{ex.Message}'. Operation will not happen: 'setPlayerCountAndMapStartup'");
            }
        });
    }
    private void setPlayerCount(int playerCount)
    {
        if (serverIdentifier <= 0) return;
        string mysqlQuery = $"UPDATE `{Config.TableName}` SET `active_players` = '{playerCount}' WHERE `id` = '{serverIdentifier}';";
        Task.Run(async () => {
            try {
                using var _connection = await ConnectAsync();
                await using var querry = new MySqlCommand(mysqlQuery, _connection);
                var result = await querry.ExecuteNonQueryAsync();
                if (result != 1) Logger?.LogError($"Error in function setPlayerCount, {result} rows affected instead of 1.");
                await _connection.CloseAsync();
            }
            catch (MySqlException ex)
            {
                Logger?.LogError($"Could not create instance of MySqlConnection, error: '{ex.Message}'. Operation will not happen: 'setPlayerCount{playerCount}'");
            }
        });
    }
    private void setShutdownInDataBase()
    {
        string mysqlQuery = $"UPDATE `{Config.TableName}` SET `active_players` = '-1' WHERE `id` = '{serverIdentifier}';";
        try
        {
            using var _connection = new MySqlConnection(ConnectionString);
            _connection.Open();
            using var querry = new MySqlCommand(mysqlQuery, _connection);
            var result = querry.ExecuteNonQuery();
            if (result != 1) Logger?.LogError($"Error in function setShutdownInDataBase, {result} rows affected instead of 1.");
            _connection.Close();
        }
        catch (MySqlException ex)
        {
            Logger?.LogError($"Could not create instance of MySqlConnection, error: '{ex.Message}'. Operation will not happen: 'setShutdownInDataBase'");
        }
    }

    public int ActivePlayersCount()
    {
        return (Utilities.GetPlayers().Where(p =>
               p.IsValid
               && !p.IsHLTV
               && !p.IsBot
               && p.Connected == PlayerConnectedState.PlayerConnected
               && p.SteamID.ToString().Length == 17)).Count();
    }

    private void loadServers()
    {
        string mysqlQuery = $"SELECT `id`,`ip`,`name`,`active_players`,`max_players`,`max_players_offset`,`map_name` FROM `{Config.TableName}`;";
        Task.Run(async () => {
            if (serverIdentifier > 0)
            {
                List<ServerInstance> tmp = new List<ServerInstance>();
                try
                {
                    using var _connection = await ConnectAsync();
                    using var querry = new MySqlCommand(mysqlQuery, _connection);
                    await using var result = await querry.ExecuteReaderAsync();
                    while (await result.ReadAsync())
                    {
                        ServerInstance instance = new ServerInstance(result.GetInt32(0), result.GetString(1), result.GetString(2), result.GetInt32(3), result.GetInt32(4), result.GetInt32(5), result.GetString(6));
                        tmp.Add(instance);
                    }
                    if (tmp.Count() < 1) Server.NextFrame(() => { Logger?.LogWarning($"Did not reload any servers from database."); });
                    await _connection.CloseAsync();
                    Servers = tmp;
                }
                catch (MySqlException ex)
                {
                    Logger?.LogError($"Could not create instance of MySqlConnection, error: '{ex.Message}'. Operation will not happen: 'loadServers'");
                }
            } else Logger?.LogWarning($"Could not reload servers becouse serverIdentifier is not set.");
        });
    }

    public void OnConfigParsed(ServersListConfig config)
    {
        Config = config;
        if (Config.RootPermissions.Length < 1) {
            Config.RootPermissions = "@css/root";
            Logger.LogWarning($"RootPermissions not set in the config, defaulting to '@css/root'");
        }
        if (Config.BasicPermissions.Length < 1) {
            Config.BasicPermissions = "@css/ban";
            Logger.LogWarning($"BasicPermissions not set in the config, defaulting to '@css/ban'");
        }
        if (Config.TableName.Length < 3) {
            Config.TableName = "serverslist_servers";
            Logger.LogWarning($"Database Table Name not set in the config, defaulting to 'serverslist_servers'");
        }
        if (Config.ServerIdFile.Length < 1)
        {
            Config.ServerIdFile = "/home/container/game/csgo/addons/counterstrikesharp/configs/id.json";
            Logger.LogWarning($"ServerIdFile not set in the config, defaulting to '/home/container/game/csgo/addons/counterstrikesharp/configs/id.json'");
        }
        if (Config.DatabaseCredentials.Length < 1)
        {
            Config.DatabaseCredentials = "/home/container/game/csgo/addons/counterstrikesharp/configs/database.json";
            Logger.LogWarning($"DatabaseCredentials not set in the config, defaulting to '/home/container/game/csgo/addons/counterstrikesharp/configs/database.json'");
        }
    }
    #endregion
}
