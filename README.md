# SteamGridDB for Xbox

[![Get it from Microsoft](https://get.microsoft.com/images/en-us%20dark.svg)](https://apps.microsoft.com/detail/9pkqx0rjc32v)

Xbox Game Bar widget to customize Xbox PC App game artwork with images from [SteamGridDB](https://www.steamgriddb.com/). Replace wrong, low-resolution or missing images for games from third-party stores (Steam, GOG, Epic Games Store, EA App and Ubisoft Connect) with high-quality artwork submitted by community.

Automatically detects correct artwork for Steam games and most popular GOG titles through SteamGridDB's platform ID matching. For Epic Games Store, EA App, Ubisoft Connect and the rest of undetected GOG games, use the built-in search feature to manually select the correct game - game names will be pre-populated where possible by using other external APIs.

Battle.net is unlikely to be ever supported because the Xbox App does not store images for Battle.net games in the same way as the other stores, and games from Battle.net usually already have high-quality cover art in the Xbox App.

The widget requires user to enable File system access under ***Settings > Privacy & security > File system > Let apps access your file system***, because by default it runs in a sandboxed environment where it is not allowed to access data of the other apps such as the Xbox App - this is the only way to enable such functionality. The only files being accessed are the Xbox App images downloaded for games installed from the third-party libraries.

### Currently known issues and/or limitations
- Demos are not supported for automatic matching, even from Steam - their ID is different from the main game, but their artwork can still be changed with manual search.
- The widget is specifically looking for square grids (512x512 or 1024x1024) or icons (which are always square), because the Xbox App is designed to show square artwork. That is why results from SteamGridDB are filtered and do not show all available images.
- Freshly uploaded SteamGridDB artwork might not show up in the widget immediately due to SteamGridDB API caching.
- Currently, there is no source to resolve names for games from the EA App.
- Sometimes the Xbox App leaves behind manifest entries for removed games, causing them to appear in the widget. Conversely, some installed games can be missing from the manifest and not show up in the widget. To solve this, delete the `ThirdPartyLibraries` folder (located in `C:\Users\{yourWindowsUsername}\AppData\Local\Packages\Microsoft.GamingApp_8wekyb3d8bbwe\LocalState\`) or the manifest files (in `ThirdPartyLibraries` subfolders for corresponding stores) — the Xbox App will recreate them correctly.
- Another current Xbox App issue: Ubisoft Connect games are not always detected. If the Xbox App library is not showing them, the widget will not be able to show them either.
- Only first 50 square grids and first 50 icons are loaded from SteamGridDB (paging is not implemented yet).

If you are building the project yourself, you will need your own SteamGridDB API key that can be obtained [here](https://www.steamgriddb.com/profile/preferences/api).

Powered by SteamGridDB API and GOG API. Not affiliated with SteamGridDB, Xbox, Steam, GOG, Epic Games, Electronic Arts, Ubisoft or their subsidiaries. All trademarks are property of their respective owners.

Credit to https://github.com/nachoaldamav/items-tracker for Epic Games Store database.

Credit to https://github.com/Haoose/UPLAY_GAME_ID for Ubisoft Connect database. 
