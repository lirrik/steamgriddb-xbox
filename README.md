# SteamGridDB for Xbox

[![Get it from Microsoft](https://get.microsoft.com/images/en-us%20dark.svg)](https://apps.microsoft.com/detail/9pkqx0rjc32v)

Xbox Game Bar widget to customize Xbox app game artwork with images from [SteamGridDB](https://www.steamgriddb.com/). Replace wrong, low-resolution or missing images for games from third-party stores (Steam, GOG, Epic Games Store and Ubisoft Connect) with high-quality artwork submitted by community.

Automatically detects Steam games and most popular GOG titles through SteamGridDB's platform ID matching. For Epic Games Store and Ubisoft Connect games, use the built-in search feature to manually select the correct game.

Battle.net is unlikely to be ever supported because the Xbox app does not store images for Battle.net games in the same way as the other stores, and games from Battle.net usually already have high-quality cover art in the Xbox app.

The widget requires user to enable File system access under ***Settings > Privacy & security > File system > Let apps access your file system***, because by default it runs in a sandboxed environment where it is not allowed to access data of the other apps such as the Xbox app - this is the only way to enable such functionality. The only files being accessed are the Xbox app images downloaded for games installed from the third-party libraries.

Currently known issues and limitations:
- Investigating if automatic game detection can be implemented for games from Epic Games Store or Ubisoft Connect.
- Sometimes the Xbox app does not fully clean up data for removed games, specifically from the manifest files - those entries will show up in the widget.
- Only first 50 square grids and first 50 icons are loaded from SteamGridDB (paging is not implemented yet).
- Controller support is not implemented yet - please use touch or mouse for now.

If you are building the project, it requires SteamGridDB API key that can be obtained [here](https://www.steamgriddb.com/profile/preferences/api).
