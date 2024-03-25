﻿using CounterStrikeSharp.API;
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
    [JsonPropertyName("ServerIp")] public string ServerIp { get; set; } = "0.0.0.0";
    [JsonPropertyName("Host")] public string Host { get; set; } = "127.0.0.1";
    [JsonPropertyName("Port")] public int Port { get; set; } = 3306;
    [JsonPropertyName("User")] public string User { get; set; } = "";
    [JsonPropertyName("Pass")] public string Pass { get; set; } = "";
    [JsonPropertyName("dBName")] public string dBName { get; set; } = "";
    [JsonPropertyName("TableName")] public string TableName { get; set; } = "serverslist_servers";
    [JsonPropertyName("BasicPermissions")] public string BasicPermissions { get; set; } = "@css/ban";
    [JsonPropertyName("RootPermissions")] public string RootPermissions { get; set; } = "@css/root";
}

public class ServersList : BasePlugin, IPluginConfig<ServersListConfig>
{
    public override string ModuleName => " ServersList";
    public override string ModuleAuthor => "NyggaBytes";
    public override string ModuleVersion => "1.1.0beta";
    public override string ModuleDescription => "";
    public ILogger? logger;
    public ServersListConfig Config { get; set; } = new();
    
    public int serverIdentifier = -1;

    private string? connectionString;
    private List<ServerInstance>? Servers = new List<ServerInstance>();

    public override void Load(bool hotReload)
    {
        logger = Logger;
        logger.LogInformation($"Plugin version: {ModuleVersion}");
        if (serverIdentifier > 0) logger.LogInformation($"Loaded plugin, hooked to id: {serverIdentifier}");

        loadServers();
        RegisterListener<Listeners.OnServerPreFatalShutdown>(setShutdownInDataBase);
        RegisterListener<Listeners.OnMapStart>(setPlayerCountAndMapStartup);
    }

    #region Commands
    [ConsoleCommand("css_serverslist", "ServersList command for admins to manage.")]
    [CommandHelper(minArgs: 1, usage: "Basic: <INFO> Root: <NAME|LIST|DELETE|RELOAD>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnServersListCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!AdminManager.PlayerHasPermissions(player, Config.BasicPermissions))
        {
            command.ReplyToCommand(string.Format(Localizer[""], Config.BasicPermissions));
            return;
        }

        if (command.ArgString == "INFO" || command.ArgString == "info")
        {
            command.ReplyToCommand($"[\u0004ServersList\u0001] Information");
            command.ReplyToCommand($" \u0004Plugin Version\u0001: " + ModuleVersion);
            command.ReplyToCommand($" \u0004ServerIdentifier\u0001: {serverIdentifier}");
            try
            {
                using var _connection = new MySqlConnection(connectionString);
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

        if (!AdminManager.PlayerHasPermissions(player, Config.RootPermissions))
        {
            command.ReplyToCommand(string.Format(Localizer[""], Config.BasicPermissions));
            return;
        }
        else {
            if (command.ArgString == "RELOAD" || command.ArgString == "reload")
            {
                OnConfigParsed(ConfigManager.Load<ServersListConfig>("ServersList"));
                command.ReplyToCommand($"[\u0004ServersList\u0001] Reloading");
                if (serverIdentifier > 0) command.ReplyToCommand($" \u0004Reloaded plugin, hooked to id: \u0007{serverIdentifier}");
                else command.ReplyToCommand($" \u0004Reloaded plugin, but \u0007couldn't hook to any id\u0004, check configuration and if specified server is present in database, to add this server use command: (css_/!)serverslist name \"NAME OF SERVER\"");
                command.ReplyToCommand($" \u0004Server Ip from config\u0001: {Config.ServerIp}");
                command.ReplyToCommand($"[/\u0004ServersList\u0001]");
                return;
            }

            if (command.ArgByIndex(1) == "name" || command.ArgByIndex(1) == "NAME")
            {
                if (command.ArgCount > 3) { command.ReplyToCommand($" \u0004usage\u0001: (css_/!)serverslist name \"NAME OF SERVER\""); return; }
                else if (command.ArgCount == 3)
                {
                    if (Config.ServerIp.Length < 1 || Config.ServerIp == "0.0.0.0") { command.ReplyToCommand("Empty or default server ip set in config, set proper one and reload plugin."); return; }
                    try
                    {
                        using var _connection = new MySqlConnection(connectionString);
                        _connection.Open();
                        using var querry = new MySqlCommand($"SELECT `id`,`ip` FROM `{Config.TableName}` WHERE `ip` = '{Config.ServerIp}'", _connection);
                        using var result = querry.ExecuteReader();
                        if (result.Read())
                        {
                            using var querryUpdate = new MySqlCommand($"UPDATE `{Config.TableName}` SET `name = '@name' WHERE `ip` = '{Config.ServerIp}';", _connection);
                            querryUpdate.Parameters.AddWithValue("@name", command.ArgByIndex(2));
                            var resultUpdate = querryUpdate.ExecuteNonQuery();
                            if (resultUpdate != 1) command.ReplyToCommand($"Error in function ServersListCommandNamUpdate, {resultUpdate} rows affected instead of 1.");
                            else if (resultUpdate == 1) command.ReplyToCommand($"Successfully updated name of current server.");
                        }
                        else {
                            using var querryInsert = new MySqlCommand($"INSERT INTO `{Config.TableName}` (`ip`,`name`) VALUES ('@ip','@name')';", _connection);
                            querryInsert.Parameters.AddWithValue("@ip", Config.ServerIp);
                            querryInsert.Parameters.AddWithValue("@name", command.ArgByIndex(2));
                            var resultInsert = querryInsert.ExecuteNonQuery();
                            if (resultInsert != 1) command.ReplyToCommand($"Error in function ServersListCommandNameInsert, {resultInsert} rows affected instead of 1.");
                            else if (resultInsert == 1) command.ReplyToCommand($"Successfully inserted record with current ip and name. Reload plugin.");
                        }
                        _connection.Close();
                    }
                    catch (MySqlException ex)
                    {
                        command.ReplyToCommand($"Could not create instance of MySqlConnection, error: '{ex.Message}'. Operation will not happen: 'ServersListCommandName'");
                    }
                    return;
                }
            }
        }
    }

    [ConsoleCommand("css_servers", "HSMANIA.net Servers list.")]
    [ConsoleCommand("css_serwery", "HSMANIA.net Lista serwerów.")]
    [CommandHelper(minArgs: 0, usage: "<NAME>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
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
                delegate(ServerInstance e) { return e.name.ToLower().Contains(command.ArgString.ToLower()); }
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
                using var _connection = new MySqlConnection(connectionString);
                await _connection.OpenAsync();
                await using var querry = new MySqlCommand(mysqlQuery, _connection);
                var result = await querry.ExecuteNonQueryAsync();
                if (result != 1) logger?.LogError($"Error in function setPlayerCountAndMapStartu, {result} rows affected instead of 1.");
                await _connection.CloseAsync();
            }
            catch (MySqlException ex)
            {
                logger?.LogError($"Could not create instance of MySqlConnection, error: '{ex.Message}'. Operation will not happen: 'setPlayerCountAndMapStartup'");
            }
        });
    }
    private void setPlayerCount(int playerCount)
    {
        if (serverIdentifier == -1) return;
        string mysqlQuery = $"UPDATE `{Config.TableName}` SET `active_players` = '{playerCount}' WHERE `id` = '{serverIdentifier}';";
        Task.Run(async () => {
            try {
                using var _connection = new MySqlConnection(connectionString);
                await _connection.OpenAsync();
                await using var querry = new MySqlCommand(mysqlQuery, _connection);
                var result = await querry.ExecuteNonQueryAsync();
                if (result != 1) logger?.LogError($"Error in function setPlayerCount, {result} rows affected instead of 1.");
                await _connection.CloseAsync();
            }
            catch (MySqlException ex)
            {
                logger?.LogError($"Could not create instance of MySqlConnection, error: '{ex.Message}'. Operation will not happen: 'setPlayerCount{playerCount}'");
            }
        });
    }
    private void setShutdownInDataBase()
    {
        string mysqlQuery = $"UPDATE `{Config.TableName}` SET `active_players` = '-1' WHERE `id` = '{serverIdentifier}';";
        try
        {
            using var _connection = new MySqlConnection(connectionString);
            _connection.Open();
            using var querry = new MySqlCommand(mysqlQuery, _connection);
            var result = querry.ExecuteNonQuery();
            if (result != 1) logger?.LogError($"Error in function setShutdownInDataBase, {result} rows affected instead of 1.");
            _connection.Close();
        }
        catch (MySqlException ex)
        {
            logger?.LogError($"Could not create instance of MySqlConnection, error: '{ex.Message}'. Operation will not happen: 'setShutdownInDataBase'");
        }
    }

    public int ActivePlayersCount()
    {
        return (Utilities.GetPlayers().Where(p =>
               p.IsValid
               && !p.IsHLTV
               && !p.IsBot
               && p.Connected == PlayerConnectedState.PlayerConnected
               && p.SteamID.ToString().Length == 17
               && (p.Team == CsTeam.Terrorist || p.Team == CsTeam.CounterTerrorist))).Count();
    }

    private void loadServers()
    {
        Servers!.Clear();
        string mysqlQuery = $"SELECT `id`,`ip`,`name`,`active_players`,`max_players`,`max_players_offset`,`map_name` FROM `{Config.TableName}`;";
        Task.Run(async () => {
            if (serverIdentifier != -1)
            {
                try
                {
                    using var _connection = new MySqlConnection(connectionString);
                    await _connection.OpenAsync();
                    using var querry = new MySqlCommand(mysqlQuery, _connection);
                    await using var result = await querry.ExecuteReaderAsync();
                    while (await result.ReadAsync())
                    {
                        ServerInstance instance = new ServerInstance(result.GetInt32(0), result.GetString(1), result.GetString(2), result.GetInt32(3), result.GetInt32(4), result.GetInt32(5), result.GetString(6));
                        Servers.Add(instance);
                    }
                    if (Servers.Count() < 1) Server.NextFrame(() => { logger?.LogWarning($"Did not reload any servers from database."); });
                    else Server.NextFrame(() => { logger?.LogInformation($"Reloaded info about {Servers.Count()} servers from database."); });
                    await _connection.CloseAsync();
                }
                catch (MySqlException ex)
                {
                    logger?.LogError($"Could not create instance of MySqlConnection, error: '{ex.Message}'. Operation will not happen: 'loadServers'");
                }
            }
        });
    }

    public void OnConfigParsed(ServersListConfig config)
    {
        logger = Logger;
        Config = config;
        if (Config.RootPermissions.Length < 1) {
            Config.RootPermissions = "@css/root";
            logger.LogWarning($"RootPermissions not set in the config, defaulting to '@css/root'");
        }
        if (Config.BasicPermissions.Length < 1) {
            Config.BasicPermissions = "@css/ban";
            logger.LogWarning($"BasicPermissions not set in the config, defaulting to '@css/ban'");
        }
        if (Config.TableName.Length < 1)
        {
            Config.TableName = "serverslist_servers";
            logger.LogWarning($"Database Table Name not set in the config, defaulting to 'serverslist_servers'");
        }
        connectionString = $"Server={Config.Host};Port={Config.Port};User ID={Config.User};Password={Config.Pass};Database={Config.dBName}";
        string mysqlQuery = $"SELECT `id` FROM `{Config.TableName}` WHERE `ip` = '{MySqlHelper.EscapeString(Config.ServerIp)}';";
        string mysqlCreateTableQuery = $"CREATE TABLE IF NOT EXISTS {Config.TableName} (id INT PRIMARY KEY AUTO_INCREMENT, ip  VARCHAR(64), name VARCHAR(64), map_name VARCHAR(64), active_players INT DEFAULT -1, max_players INT, max_players_offset INT DEFAULT 0); ALTER TABLE {Config.TableName} AUTO_INCREMENT=1;";
        if (Config.ServerIp.Length == 0)
        {
            serverIdentifier = -1;
            logger.LogCritical("ServerIp specified in the config is empty. Plugin won't work.");
        }
        else
        {
            try
            {
                using var _connection = new MySqlConnection(connectionString);
                _connection.Open();
                using var querry = new MySqlCommand(mysqlQuery, _connection);
                using var result = querry.ExecuteReader();
                if (result.Read()) { serverIdentifier = result.GetInt32(0); }
                else
                {
                    serverIdentifier = -1;
                    logger?.LogCritical("This server ip specified don't exist in the database. Plugin won't work.");
                }
                _connection.Close();
            }
            catch (MySqlException ex)
            {
                if (ex.ErrorCode == MySqlErrorCode.NoSuchTable)
                {
                    try
                    {
                        using var _connection = new MySqlConnection(connectionString);
                        _connection.Open();
                        using var querry = new MySqlCommand(mysqlCreateTableQuery, _connection);
                        querry.ExecuteNonQuery();
                        logger.LogInformation($"Table not found in database, created one.");
                        logger.LogInformation($"You need to insert data about servers into database for plugin to work. To add this server use command: (css_/!)serverslist name \"NAME OF SERVER\"");
                        serverIdentifier = -1;
                        _connection.Close();
                    }
                    catch (MySqlException exCreate) {
                        logger?.LogError($"Could not create instance of MySqlConnection, error: '{exCreate.Message}'. Operation will not happen: 'OnConfigParsedCreateTableIfNotExist'");
                    }
                }
                else {
                    serverIdentifier = -1;
                    logger?.LogError($"Could not create instance of MySqlConnection, error: '{ex.Message}'. Operation will not happen: 'OnConfigParsed'");
                }
            }
        }
    }
    #endregion
}
