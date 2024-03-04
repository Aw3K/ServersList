﻿using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
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
    public required string mapName { get; set; }
    [SetsRequiredMembers]
    public ServerInstance(int id, string ip, string name, int teamCount, string mapName) =>
    (this.id, this.ip, this.name, this.teamCount, this.mapName) = (id, ip, name, teamCount, mapName);
}

public class LiveInfo {
    public required int teamCount { get; set; }
    public required string mapName { get; set; }
    [SetsRequiredMembers]
    public LiveInfo(int teamCount, string mapName) =>
        (this.teamCount, this.mapName) = (teamCount, mapName);
}

public class ServersListConfig : BasePluginConfig
{
    [JsonPropertyName("ServerIp")] public string ServerIp { get; set; } = "0.0.0.0";
    [JsonPropertyName("Host")] public string Host { get; set; } = "127.0.0.1";
    [JsonPropertyName("Port")] public int Port { get; set; } = 3306;
    [JsonPropertyName("User")] public string User { get; set; } = "";
    [JsonPropertyName("Pass")] public string Pass { get; set; } = "";
    [JsonPropertyName("dBName")] public string dBName { get; set; } = "";
}

public class ServersList : BasePlugin, IPluginConfig<ServersListConfig>
{
    public override string ModuleName => "ServersList";
    public override string ModuleAuthor => "NyggaBytes";
    public override string ModuleVersion => "1.0.3";
    public ServersListConfig Config { get; set; } = new();
    
    public int serverIdentifier = 0;

    private MySqlConnection? _connection;
    private string? connectionString;
    private List<ServerInstance>? Servers = new List<ServerInstance>();

    public override void Load(bool hotReload)
    {
        if (serverIdentifier > 0) Server.PrintToConsole($"[ServersList] SUCCESS: Loaded plugin, hooked to id: {serverIdentifier}");

        loadServers();
        if (Servers!.Count() > 0) { Server.PrintToConsole($"[ServersList] SUCCESS: Loaded {Servers!.Count()} servers from database(including this)."); }
        else { Server.PrintToConsole($"[ServersList] WARN: Did not loaded any servers from Database."); }

        RegisterListener<Listeners.OnServerPreFatalShutdown>(setShutdownInDataBase);
        RegisterListener<Listeners.OnMapStart>(setPlayerCountAndMapStartup);
        base.Load(hotReload);
    }

    #region Commands
    [ConsoleCommand("css_servers", "HSMANIA.net Servers list.")]
    [ConsoleCommand("css_serwery", "HSMANIA.net Lista serwerów.")]
    [CommandHelper(minArgs: 0, usage: "<NAME>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnServersCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!databaseConnect()) {
            command.ReplyToCommand(Localizer["DatabaseError"]);
            return;
        }
        command.ReplyToCommand(" ");
        command.ReplyToCommand("\u0020\u0020\u0004HS\u0006MAN\u0005IA\u0001.net");
        command.ReplyToCommand(" ");
        if (command.ArgString != "")
        {
            List<ServerInstance> founded = Servers!.FindAll(
                delegate(ServerInstance e) { return e.name.ToLower().Contains(command.ArgString.ToLower()); }
            );
            if (founded.Count() < 1) command.ReplyToCommand(Localizer["NotFoundAny"]);
            else if (founded.Count() == 1)
            {
                if (founded.First().teamCount == -1) command.ReplyToCommand($" \u0004|> \u0001{founded.First().name} \u0004| \u0007 OFFLINE");
                else
                {
                    if (founded.First().id != serverIdentifier)
                    {
                        command.ReplyToCommand($" \u0004|> \u0001{founded.First().name} \u0004| ONLINE \u0001" + Localizer["CurrentPlaying"] + "\u0004: \u000C" + founded.First().teamCount + "\u0020\u0004|\u0020\u000C" + founded.First().mapName);
                        command.ReplyToCommand(string.Format(Localizer["ConnectWith"], founded.First().ip));
                    }
                    else { command.ReplyToCommand(Localizer["InfoOwn"] + founded.First().ip); }
                }
            }
            else foreach(var server in founded) replyToCommandList(command, server.id, server.ip, server.name, server.teamCount, server.mapName);
        } else foreach (var server in Servers!) replyToCommandList(command, server.id, server.ip, server.name, server.teamCount, server.mapName);
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
    private bool databaseConnect(bool req = false) {
        if (serverIdentifier == -1) { return false; }
        if (_connection == null) { _connection = new MySqlConnection(connectionString); }
        if (_connection.State == ConnectionState.Open) { return true; }
        else if (_connection.State == ConnectionState.Closed && req == false) {
            _connection.Open();
            return databaseConnect(true);
        }
        else if (_connection.State == ConnectionState.Broken && req == false) {
            _connection.Close();
            _connection.Open();
            return databaseConnect(true);
        }
        return false;
    }

    public void replyToCommandList(CommandInfo command, int id, string ip, string name, int playerCount, string mapName) {
        if (playerCount == -1) command.ReplyToCommand($" \u0004|> \u0001{name} \u0004| \u0007 OFFLINE");
        else if (playerCount == -2) command.ReplyToCommand($" \u0004|> \u0001{name} \u0004| \u0007 " + Localizer["DatabaseErrList"]);
        else {
            if (id == serverIdentifier) { command.ReplyToCommand($" \u0004|> \u0001{name} \u0004| \u0001" + Localizer["CurrentInfoOwn"] + " \u0004| \u0001" + ip); }
            else command.ReplyToCommand($" \u0004|> \u0001{name} \u0004| ONLINE \u0001[\u000C{mapName}\u0001] " + Localizer["CurrentPlaying"] + "\u0004: \u000C" + playerCount + " \u0004| \u0001" + ip);
        }
    }
    private void setPlayerCountAndMapStartup(string mapName) {
        if (databaseConnect())
        {
            using var querry = new MySqlCommand($"UPDATE `lvl_web_servers` SET `active_players` = '0', `map_name` = '{mapName}' WHERE `id` = '{serverIdentifier}';", _connection);
            querry.ExecuteNonQuery();
            querry.Dispose();
        }
    }
    private void setPlayerCount(int playerCount)
    {
        if (databaseConnect())
        {
            using var querry = new MySqlCommand($"UPDATE `lvl_web_servers` SET `active_players` = '{playerCount}' WHERE `id` = '{serverIdentifier}';", _connection);
            querry.ExecuteNonQuery();
            querry.Dispose();
        }
    }
    private void setShutdownInDataBase()
    {
        if (databaseConnect())
        {
            using var querry = new MySqlCommand($"UPDATE `lvl_web_servers` SET `active_players` = '-1' WHERE `id` = '{serverIdentifier}';", _connection);
            querry.ExecuteNonQuery();
            querry.Dispose();
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
        if (databaseConnect())
        {
            using (var query = new MySqlCommand($"SELECT `id`,`ip`,`name`,`active_players`,`map_name` FROM `lvl_web_servers`;", _connection))
            {
                using var result = query.ExecuteReader();
                while (result.Read())
                {
                    ServerInstance instance = new ServerInstance(result.GetInt32(0), result.GetString(1), result.GetString(2), result.GetInt32(3), result.GetString(4));
                    Servers.Add(instance);
                }
                result.Close();
                result.Dispose();
            }
        }
    }

    public void OnConfigParsed(ServersListConfig config)
    {
        Config = config;
        connectionString = $"Server={Config.Host};User ID={Config.User};Password={Config.Pass};Database={Config.dBName}";
        if (databaseConnect())
        {
            using (var query = new MySqlCommand($"SELECT `id` FROM `lvl_web_servers` WHERE `ip` = '{MySqlHelper.EscapeString(Config.ServerIp)}';", _connection).ExecuteReader())
            {
                if (query.Read()) { serverIdentifier = query.GetInt32(0); }
                else {
                    serverIdentifier = -1;
                    throw new Exception("This server ip specified don't exist in the Database.");
                }
                query.Close();
                query.Dispose();
            }
        } else {
            serverIdentifier = -1;
            throw new Exception("Database connection error.");
        }
    }
    #endregion
}
