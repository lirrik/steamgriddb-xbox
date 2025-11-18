# SteamGridDB for Xbox

[![Get it from Microsoft](https://get.microsoft.com/images/en-us%20dark.svg)](https://apps.microsoft.com/detail/9pkqx0rjc32v)

Xbox Game Bar widget to customize Xbox PC app game artwork with images from [SteamGridDB](https://www.steamgriddb.com/). Replace wrong, low-resolution or missing images for games from third-party stores (Steam, GOG, Epic Games Store and Ubisoft Connect) with high-quality artwork submitted by community.

Automatically detects Steam games and most popular GOG titles through SteamGridDB's platform ID matching. For Epic Games Store and Ubisoft Connect games, use the built-in search feature to manually select the correct game.

Battle.net is unlikely to be ever supported because the Xbox app does not store images for Battle.net games in the same way as the other stores, and games from Battle.net usually already have high-quality cover art in the Xbox app.

The widget requires user to enable File system access under ***Settings > Privacy & security > File system > Let apps access your file system***, because by default it runs in a sandboxed environment where it is not allowed to access data of the other apps such as the Xbox app - this is the only way to enable such functionality. The only files being accessed are the Xbox app images downloaded for games installed from the third-party libraries.

### Currently known issues and/or limitations
- Demos are not supported for automatic matching, even from Steam - their ID is different from the main game, but their artwork can still be changed with manual search.
- The widget is specifically looking for square grids (512x512 or 1024x1024) or icons (which are always square), because the Xbox app is designed to show square artwork. That is why results from SteamGridDB are filtered and do not show all available images.
- Investigating if automatic game detection can be implemented for games from Epic Games Store or Ubisoft Connect.
- Sometimes the Xbox app does not fully clean up data for removed games, specifically from the manifest files - those entries will show up in the widget. Sometimes some installed games will be missing from the manifest files and not show up in the widget. The best solution is to delete your ThirdParty folder or the manifest files - Xbox app will recreate them and that will fix it.
- Only first 50 square grids and first 50 icons are loaded from SteamGridDB (paging is not implemented yet).
- Controller support is not implemented yet - please use touch or mouse for now.

If you are building the project, it requires SteamGridDB API key that can be obtained [here](https://www.steamgriddb.com/profile/preferences/api).

Powered by SteamGridDB API. Not affiliated with SteamGridDB, Xbox, Steam, GOG, Epic Games, Ubisoft, Electronic Arts or their subsidiaries. All trademarks are property of their respective owners.
