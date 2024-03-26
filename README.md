ServersList is a cssharp plugin for setting and displaying servers information, searching for a servers from list based on typed name.

For plugin to work, you must have MySql database, set credentials in plugin config(located at cssharp folder/configs/plugins/ServersList/ServersList.json), table will be created if not exist, plugin must be on every server for them to set live data into database.

You can use existing table, but must include needed columns for it to work. A lot of information about plugin will be logged on server console so remember to check it often if some weird behaviours occurs.

Configuration:

{ //cssharp folder/configs/plugins/ServersList/ServersList.json
  
  "ServerIp": "0.0.0.0", <- Ip of a server plugin will be on, for identification purpose
  
  "Host": "", <- MySql database host
  
  "Port": 3306, <- MySql database port
  
  "User": "", <- MySql database user
  
  "Pass": "", <- MySql database users password
  
  "dBName": "", <- MySql database name
  
  "TableName": "serverslist_servers", <- MySql database table name
  
  "BasicPermissions": "@css/ban", <- cssharp permissions flags for access to basic plugin commands
  
  "RootPermissions": "@css/root", <- cssharp permissions flags for access to advanced plugin commands, watch out who You give access to
  
  "ConfigVersion": 1
  
}

On map load plugin will save info about max players, most of servers owners will set it more than available slots in tt/ct so you can change it per server.

In database theres column "max_players_offset", you can set value there that will be added to max servers when list will be displayed after using css_servers, for example if server max_players is set to 13 and can only be played 5vs5, you can set offset at -3 for it to display 10.

Servers must be added in database, only name and ip must be set, id should be generated automatically and other values will be set by plugin.

As of v1.1.0, theres now an option for inserting server into database using commands, same as deleting or changing offset. Look commands below.

All lines outputted by plugin can be set in langs, instructions on how to do it are in project as file, including logo, colors and more.

Commands:

- css_servers/css_serwery <name> <- searches for specyfic servers or displays whole list if no argument is set, no permission needed for it to use
  
(css_/!)serverslist \<OPTION\> \<ARGUMENTS\>

BASIC OPTIONS:

-  \<HELP\> - displays list of commands
-  \<INFO\> - displays information about plugin such as version/database/permissions
-  \<RSERVERS\> - run async task for servers reload from database

ROOT OPTIONS:

-  \<LIST\> - gathers and displays raw data about servers from database
-  \<NAME\> \<"NAME OF SERVER"\> - updates name of current server or inserts row into database if not exist, name must be inside ""
-  \<DELETE\> \<SERVER ID\> - deletes record of server from database with a given id
-  \<OFFSET\> \<SERVER ID\> \<NEW VALUE\> - change value of max_players_offset in database at given server id
-  \<RELOAD\> - reloads plugin configuration, hooks again to server id

Example how list will look like after typing css_servers | !servers

![obraz](https://github.com/HSMANIA-net/ServersList/assets/37087934/2fba1e0c-2f60-4767-871d-544723d5357c)

Example how list will look like after searching for specyfic server e.g. typing css_servers ffa | !servers ffa

![obraz](https://github.com/HSMANIA-net/ServersList/assets/37087934/94ea79c1-bae9-480e-acee-f1000e7ae0fd)
