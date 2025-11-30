using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Windows.Data.Json;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Web.Http;

using SteamGridDB.Xbox.Models;
using SteamGridDB.Xbox.Services.SteamGridDB;
using SteamGridDB.Xbox.Services.SteamGridDB.Models;

namespace SteamGridDB.Xbox
{
    /// <summary>
    /// Primary widget page that loads and displays Xbox App third-party games.
    /// </summary>
    public sealed partial class PrimaryWidget : Page, INotifyPropertyChanged
    {
        public ObservableCollection<GameEntry> GameEntries
        {
            get; set;
        }

        private readonly string steamGridDbApiKey = Environment.GetEnvironmentVariable("STEAMGRIDDB_API_KEY");
        private const string unknownName = "Unknown";

        private static Dictionary<string, string> ubisoftGameLookupCache = null;
        private static readonly Dictionary<string, string> gogNameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> epicNameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HttpClient sharedHttpClient = new HttpClient();

        private StorageFolder currentGameFolder;

        private GameEntry currentSelectedGame;
        public GameEntry CurrentSelectedGame
        {
            get => currentSelectedGame;
            set
            {
                if (currentSelectedGame != value)
                {
                    currentSelectedGame = value;
                    OnPropertyChanged(nameof(CurrentSelectedGame));
                }
            }
        }

        private string gridPanelHeaderText;
        public string GridPanelHeaderText
        {
            get => gridPanelHeaderText;
            set
            {
                if (gridPanelHeaderText != value)
                {
                    gridPanelHeaderText = value;
                    OnPropertyChanged(nameof(GridPanelHeaderText));
                }
            }
        }

        private string searchPanelHeaderText;
        public string SearchPanelHeaderText
        {
            get => searchPanelHeaderText;
            set
            {
                if (searchPanelHeaderText != value)
                {
                    searchPanelHeaderText = value;
                    OnPropertyChanged(nameof(SearchPanelHeaderText));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public PrimaryWidget()
        {
            InitializeComponent();
            GameEntries = new ObservableCollection<GameEntry>();
            Loaded += PrimaryWidget_Loaded;
        }

        private async void PrimaryWidget_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadGameEntriesAsync();
            
            // Set default focus to Fix my library button for controller navigation
            FixLibraryButton.Focus(FocusState.Programmatic);
        }

        private async Task<StorageFolder> GetThirdPartyLibrariesFolderAsync()
        {
            // Direct access to the standard location
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string thirdPartyLibrariesPath = Path.Combine(userProfile,
                 @"AppData\Local\Packages\Microsoft.GamingApp_8wekyb3d8bbwe\LocalState\ThirdPartyLibraries");

            try
            {
                // Try to get folder directly with broadFileSystemAccess permission
                return await StorageFolder.GetFolderFromPathAsync(thirdPartyLibrariesPath);
            }
            catch (UnauthorizedAccessException)
            {
                // Access denied - user needs to grant permission in Windows Settings
                return null;
            }
            catch (FileNotFoundException)
            {
                // Directory doesn't exist
                throw new DirectoryNotFoundException($"ThirdPartyLibraries folder not found at: {thirdPartyLibrariesPath}");
            }
            catch
            {
                // Other error
                return null;
            }
        }

        private async Task LoadGameEntriesAsync()
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    StatusText.Text = $"Attempting to access ThirdPartyLibraries...";
                    InstructionsPanel.Visibility = Visibility.Collapsed;
                    GameEntriesListView.Visibility = Visibility.Visible;
                });

                StorageFolder thirdPartyFolder = null;

                try
                {
                    thirdPartyFolder = await GetThirdPartyLibrariesFolderAsync();
                }
                catch (DirectoryNotFoundException)
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        StatusText.Text = "ThirdPartyLibraries folder was not found. Make sure games are added to the Xbox App.";
                        GameEntriesListView.Visibility = Visibility.Collapsed;
                    });

                    return;
                }

                if (thirdPartyFolder == null)
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        StatusText.Text = "Access denied. Please grant file system permission.";
                        InstructionsPanel.Visibility = Visibility.Visible;
                        GameEntriesListView.Visibility = Visibility.Collapsed;
                    });

                    return;
                }

                // Get all subdirectories
                var folders = await thirdPartyFolder.GetFoldersAsync();

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    var directoryNames = string.Join(", ", folders.Select(f => f.Name));
                    StatusText.Text = $"Found {folders.Count} director{(folders.Count == 1 ? "y" : "ies")} ({directoryNames}). Loading and sorting...";
                });

                // Temporary list to collect games before sorting
                var tempGameList = new List<GameEntry>();

                // Check if API key is available
                if (string.IsNullOrEmpty(steamGridDbApiKey))
                {
                    StatusText.Text = "Error: SteamGridDB API key is not set.";
                }

                using (var sgdbClient = new SteamGridDbClient(steamGridDbApiKey))
                {
                    foreach (var folder in folders)
                    {
                        string directoryName = folder.Name;

                        if (directoryName == "bnet")
                        {
                            // Skip Battle.net folder as it is not currently supported - Xbox App does not store images here
                            continue;
                        }

                        string manifestFileName = $"{directoryName}.manifest";

                        try
                        {
                            // Try to get the manifest file
                            StorageFile manifestFile = await folder.GetFileAsync(manifestFileName);

                            // Read and parse the manifest JSON file
                            string jsonContent = await FileIO.ReadTextAsync(manifestFile);


                            if (JsonObject.TryParse(jsonContent, out JsonObject root))
                            {
                                // Check if gameCache exists in the root
                                if (!root.ContainsKey("gameCache"))
                                {
                                    continue;
                                }

                                // Get the gameCache object
                                if (root.GetNamedValue("gameCache").ValueType != JsonValueType.Object)
                                {
                                    continue;
                                }

                                JsonObject gameCache = root.GetNamedObject("gameCache");

                                // Iterate through all entries in the gameCache
                                foreach (var entry in gameCache)
                                {
                                    // Skip the "version" property if it exists
                                    if (entry.Key == "version")
                                    {
                                        continue;
                                    }

                                    // Only process entries that are objects
                                    if (entry.Value.ValueType != JsonValueType.Object)
                                    {
                                        continue;
                                    }

                                    JsonObject entryObject = entry.Value.GetObject();

                                    // Only process entries that have an "id" property
                                    if (!entryObject.ContainsKey("id"))
                                    {
                                        continue;
                                    }

                                    // Get the ID from the "id" property (not from the key)
                                    string entryId = entryObject.GetNamedString("id");

                                    // Parse addedDate - it's stored as a string in JSON
                                    string addedDateString = entryObject.GetNamedString("addedDate", "0");
                                    long timestamp = 0;

                                    if (!string.IsNullOrEmpty(addedDateString) && long.TryParse(addedDateString, out long parsedTimestamp))
                                    {
                                        timestamp = parsedTimestamp;
                                    }

                                    // Convert ID to image filename (replace : with _)
                                    string imageFileName = entryId.Replace(":", "_") + ".png";
                                    string backupFileName = entryId.Replace(":", "_") + ".bak";
                                    string imageName = imageFileName;

                                    BitmapImage image = null;
                                    bool hasBackup = false;

                                    // Check if backup exists
                                    try
                                    {
                                        await folder.GetFileAsync(backupFileName);
                                        hasBackup = true;
                                    }
                                    catch (FileNotFoundException)
                                    {
                                        // Backup doesn't exist, that's okay
                                    }

                                    // Load image on background thread, create BitmapImage on UI thread
                                    IRandomAccessStream imageStream = null;
                                    try
                                    {
                                        StorageFile imageFile = await folder.GetFileAsync(imageFileName);
                                        imageStream = await imageFile.OpenReadAsync();

                                        // Create and set BitmapImage on UI thread because it has to be owned by it
                                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                                        {
                                            try
                                            {
                                                image = new BitmapImage();
                                                // Fire-and-forget: async call will complete in background
                                                var _ = image.SetSourceAsync(imageStream);
                                            }
                                            catch
                                            {
                                                // Image loading failed, will be handled below
                                                image = null;
                                                imageStream?.Dispose();
                                            }
                                        });
                                    }
                                    catch (FileNotFoundException)
                                    {
                                        // Image doesn't exist, that's okay
                                        imageName = "Not found";
                                        imageStream?.Dispose();
                                    }

                                    // Try to fetch game name from SteamGridDB API
                                    string gameName = unknownName;
                                    GamePlatform platform = GamePlatformHelper.FromXboxDirectory(directoryName);
                                    string xboxPlatformId = entryId.Substring(entryId.IndexOf(':') + 1);
                                    string externalPlatformId = xboxPlatformId;

                                    if (platform == GamePlatform.Epic)
                                    {
                                        // For Epic, entryId format is "epic:namespace:ID"
                                        var parts = entryId.Split(':');

                                        if (parts.Length >= 3)
                                        {
                                            externalPlatformId = parts[2];
                                        }
                                    }

                                    bool hasSteamGridDBMatch = false;

                                    try
                                    {
                                        string platformString = GamePlatformHelper.GamePlatformToSGDBApiString(platform);

                                        if (!string.IsNullOrEmpty(platformString))
                                        {
                                            var gameInfo = await sgdbClient.GetGameByPlatformIdAsync(platformString, externalPlatformId);

                                            if (gameInfo != null && !string.IsNullOrEmpty(gameInfo.Name))
                                            {
                                                gameName = gameInfo.Name;
                                                hasSteamGridDBMatch = true;
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        // Log but don't fail - game name is optional, default is "Unknown"
                                        System.Diagnostics.Debug.WriteLine($"Could not fetch game name for {entryId}: {ex.Message}");
                                    }

                                    if (!hasSteamGridDBMatch)
                                    {
                                        if (platform == GamePlatform.GOG)
                                        {
                                            if (!gogNameCache.TryGetValue(externalPlatformId, out string gogName) || string.IsNullOrEmpty(gogName))
                                            {
                                                gogName = await GetGogGameNameAsync(externalPlatformId);

                                                if (!string.IsNullOrEmpty(gogName))
                                                {
                                                    gogNameCache[externalPlatformId] = gogName;
                                                    gameName = gogName;
                                                }
                                            }
                                            else
                                            {
                                                gameName = gogName;
                                            }
                                        }
                                        else if (platform == GamePlatform.Epic)
                                        {
                                            if (!epicNameCache.TryGetValue(externalPlatformId, out string epicName) || string.IsNullOrEmpty(epicName))
                                            {
                                                epicName = await GetEpicGameNameAsync(externalPlatformId);

                                                if (!string.IsNullOrEmpty(epicName))
                                                {
                                                    epicNameCache[externalPlatformId] = epicName;
                                                    gameName = epicName;
                                                }
                                            }
                                            else
                                            {
                                                gameName = epicName;
                                            }
                                        }
                                        else if (platform == GamePlatform.Ubisoft)
                                        {
                                            var ubisoftName = await GetUbisoftGameNameAsync(externalPlatformId);

                                            if (!string.IsNullOrEmpty(ubisoftName))
                                            {
                                                gameName = ubisoftName;
                                            }
                                        }
                                        else if (platform == GamePlatform.EA)
                                        {
                                            // TODO: Implement EA App name fetching if possible
                                        }
                                    }

                                    // Add to temporary list instead of directly to GameEntries
                                    tempGameList.Add(new GameEntry
                                    {
                                        Name = gameName,
                                        XboxPlatformId = xboxPlatformId,
                                        ExternalPlatformId = externalPlatformId,
                                        ImageFileName = imageName,
                                        Platform = platform,
                                        AddedDate = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime,
                                        Directory = directoryName,
                                        Image = image,
                                        HasBackup = hasBackup,
                                        HasSteamGridDBMatch = hasSteamGridDBMatch
                                    });
                                }
                            }
                        }
                        catch (FileNotFoundException)
                        {
                            // Manifest file doesn't exist in this directory, skip it
                            continue;
                        }
                        catch (Exception ex)
                        {
                            // Log error but continue processing other directories
                            System.Diagnostics.Debug.WriteLine($"Error processing {directoryName}: {ex.Message}");
                        }
                    }
                }

                // Sort games alphabetically by name, with "Unknown" at the end
                var sortedGames = tempGameList
                    .OrderBy(g => g.Name == unknownName ? 1 : 0)
                    .ThenBy(g => g.Name)
                    .ToList();

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    foreach (var game in sortedGames)
                    {
                        GameEntries.Add(game);
                    }

                    StatusText.Text = $"Found {GameEntries.Count} game{(GameEntries.Count == 1 ? string.Empty : "s")} from third-party stores";
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    StatusText.Text = $"Error: {ex.Message}";
                });
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            GameEntries.Clear();
            await LoadGameEntriesAsync();
        }

        /// <summary>
        /// Handle fix library button click to automatically download artwork for all eligible games
        /// </summary>
        private async void FixLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            // Show confirmation dialog
            ContentDialog confirmDialog = new ContentDialog
            {
                Title = "Fix my library",
                Content = "This will automatically download the highest-scored artwork from SteamGridDB for all games that have a direct SteamGridDB match and have not been manually modified yet.\n\n" +
                          "Original images will be backed up and can be restored later.\n\n" +
                          "Do you want to continue?",
                PrimaryButtonText = "Fix my library",
                CloseButtonText = "Cancel",
                Style = Resources["DarkContentDialogStyle"] as Style,
                PrimaryButtonStyle = Resources["ContentDialogButtonStyle"] as Style,
                CloseButtonStyle = Resources["ContentDialogButtonStyle"] as Style
            };

            // Set XamlRoot for proper dialog display
            if (Windows.Foundation.Metadata.ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 8))
            {
                confirmDialog.XamlRoot = Content.XamlRoot;
            }

            ContentDialogResult result = await confirmDialog.ShowAsync();

            // Only proceed if user clicked the primary button
            if (result == ContentDialogResult.Primary)
            {
                await FixLibraryAsync();
            }
        }

        /// <summary>
        /// Automatically downloads the highest-scored artwork for games with a match in SteamGridDB and no backup.
        /// </summary>
        private async Task FixLibraryAsync()
        {
            try
            {
                // Get eligible games: there is a match in SteamGridDB and no backup
                var eligibleGames = GameEntries.Where(g => g.HasSteamGridDBMatch && !g.HasBackup).ToList();

                if (eligibleGames.Count == 0)
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        StatusText.Text = "No eligible artworks to fix (all games either were already modified or have no match in SteamGridDB)";
                    });

                    return;
                }

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    StatusText.Text = $"Fixing library artwork: processing {eligibleGames.Count} game{(eligibleGames.Count == 1 ? "" : "s")}...";
                });

                int successCount = 0;
                int skipCount = 0;
                int errorCount = 0;

                using (var client = new SteamGridDbClient(steamGridDbApiKey))
                {
                    foreach (var game in eligibleGames)
                    {
                        try
                        {
                            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                            {
                                StatusText.Text = $"Processing {game.Name} ({successCount + skipCount + errorCount + 1}/{eligibleGames.Count})...";
                            });

                            // Get the platform string for SteamGridDB API
                            string platformString = GamePlatformHelper.GamePlatformToSGDBApiString(game.Platform);

                            if (string.IsNullOrEmpty(platformString))
                            {
                                skipCount++;
                                System.Diagnostics.Debug.WriteLine($"Skipping {game.Name}: unsupported platform");

                                continue;
                            }

                            // Fetch grids and icons from SteamGridDB
                            var grids = await client.GetSquareGridsByPlatformIdAsync(platformString, game.XboxPlatformId);
                            var icons = await client.GetSquareIconsByPlatformIdAsync(platformString, game.XboxPlatformId);

                            // Try grids first
                            if (grids != null && grids.Count > 0)
                            {
                                // Get the highest-scored grid
                                var bestGrid = grids.OrderByDescending(g => g.Score).First();
                                bool downloaded = await DownloadAndReplaceImageForGameAsync(game, bestGrid.Url);

                                if (downloaded)
                                {
                                    successCount++;
                                }
                                else
                                {
                                    errorCount++;
                                }
                            }
                            // If no grids, try icons
                            else if (icons != null && icons.Count > 0)
                            {
                                // Get the highest-scored icon
                                var bestIcon = icons.OrderByDescending(i => i.Score).First();
                                bool downloaded = await DownloadAndReplaceImageForGameAsync(game, bestIcon.Url);

                                if (downloaded)
                                {
                                    successCount++;
                                }
                                else
                                {
                                    errorCount++;
                                }
                            }
                            else
                            {
                                skipCount++;
                                System.Diagnostics.Debug.WriteLine($"No artwork found for {game.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            errorCount++;
                            System.Diagnostics.Debug.WriteLine($"Error processing {game.Name}: {ex.Message}");
                        }
                    }
                }

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    StatusText.Text = $"Fixing library is complete: {successCount} updated, {skipCount} skipped, {errorCount} error{(errorCount == 1 ? string.Empty : "s")}";
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    StatusText.Text = $"Error fixing library: {ex.Message}";
                });

                System.Diagnostics.Debug.WriteLine($"Error in FixLibraryAsync: {ex.Message}");
            }
        }

        /// <summary>
        /// Downloads and replaces an image for a specific game
        /// </summary>
        /// <param name="game">The game to update</param>
        /// <param name="imageUrl">The URL of the image to download</param>
        /// <returns>True if successful, false otherwise</returns>
        private async Task<bool> DownloadAndReplaceImageForGameAsync(GameEntry game, string imageUrl)
        {
            try
            {
                // Find the game folder
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string thirdPartyLibrariesPath = Path.Combine(userProfile,
                    @"AppData\Local\Packages\Microsoft.GamingApp_8wekyb3d8bbwe\LocalState\ThirdPartyLibraries");
                string gameFolderPath = Path.Combine(thirdPartyLibrariesPath, game.Directory);

                StorageFolder gameFolder = null;

                try
                {
                    gameFolder = await StorageFolder.GetFolderFromPathAsync(gameFolderPath);
                }
                catch
                {
                    return false;
                }

                // Reuse the common download and replace logic
                return await DownloadAndReplaceImageCoreAsync(game, gameFolder, imageUrl, updateStatusText: false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error downloading image for {game.Name}: {ex.Message}");

                return false;
            }
        }

        /// <summary>
        /// Core logic for downloading and replacing a game's image
        /// </summary>
        /// <param name="game">The game to update</param>
        /// <param name="gameFolder">The game's storage folder</param>
        /// <param name="imageUrl">The URL of the image to download</param>
        /// <param name="updateStatusText">Whether to update the main status text</param>
        /// <returns>True if successful, false otherwise</returns>
        private async Task<bool> DownloadAndReplaceImageCoreAsync(GameEntry game, StorageFolder gameFolder, string imageUrl, bool updateStatusText = true)
        {
            try
            {
                // Download the image
                using (var httpClient = new HttpClient())
                {
                    var response = await httpClient.GetAsync(new Uri(imageUrl));

                    if (!response.IsSuccessStatusCode)
                    {
                        return false;
                    }

                    var imageBytes = await response.Content.ReadAsBufferAsync();

                    // Generate the filenames
                    string imageFileName = $"{GamePlatformHelper.ToXboxDirectory(game.Platform)}_{game.XboxPlatformId.Replace(":", "_")}.png";
                    string backupFileName = $"{GamePlatformHelper.ToXboxDirectory(game.Platform)}_{game.XboxPlatformId.Replace(":", "_")}.bak";

                    // Create backup of ORIGINAL image ONLY if backup doesn't already exist
                    bool backupExists = false;

                    try
                    {
                        await gameFolder.GetFileAsync(backupFileName);
                        backupExists = true;
                    }
                    catch (FileNotFoundException)
                    {
                        // Backup doesn't exist, create it from current image
                        try
                        {
                            var existingImageFile = await gameFolder.GetFileAsync(imageFileName);

                            // Backup the ORIGINAL image by copying to preserve it
                            var backupFile = await gameFolder.CreateFileAsync(backupFileName, CreationCollisionOption.ReplaceExisting);
                            backupExists = true;
                            var existingBuffer = await FileIO.ReadBufferAsync(existingImageFile);
                            await FileIO.WriteBufferAsync(backupFile, existingBuffer);
                        }
                        catch (FileNotFoundException)
                        {
                            // No existing image to backup
                        }
                    }

                    // Save the new image (replaces current)
                    var imageFile = await gameFolder.CreateFileAsync(imageFileName, CreationCollisionOption.ReplaceExisting);
                    await FileIO.WriteBufferAsync(imageFile, imageBytes);

                    // Reload the image in the UI - open stream before dispatching to UI thread
                    var stream = await imageFile.OpenReadAsync();

                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        try
                        {
                            var newImage = new BitmapImage();
                            // Fire-and-forget: async call will complete in background
                            var _ = newImage.SetSourceAsync(stream);

                            game.Image = newImage;
                            game.ImageFileName = imageFileName;
                            game.HasBackup = backupExists;

                            if (updateStatusText)
                            {
                                if (game.Name == unknownName)
                                {
                                    StatusText.Text = $"Artwork {imageFileName} updated successfully";
                                }
                                else
                                {
                                    StatusText.Text = $"Artwork for {game.Name} updated successfully";
                                }
                            }
                        }
                        catch
                        {
                            stream?.Dispose();
                        }
                    });

                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in DownloadAndReplaceImageCoreAsync for {game.Name}: {ex.Message}");

                return false;
            }
        }

        /// <summary>
        /// Handle edit button click to show grid selection panel
        /// </summary>
        private async void EditGameImage_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;

            if (button?.Tag is GameEntry gameEntry)
            {
                CurrentSelectedGame = gameEntry;

                // Find the folder for this game
                await LoadGridSelectionPanelAsync(gameEntry);
            }
        }

        /// <summary>
        /// Load and display available grids for the selected game
        /// </summary>
        private async Task LoadGridSelectionPanelAsync(GameEntry game)
        {
            try
            {
                // Update panel header with game info
                GridPanelHeaderText = $"Select artwork for {game.Name} (platform: {game.Platform}, ID: {game.XboxPlatformId})";

                // Show panel with animation
                await ShowGridPanelAsync();

                // Show loading indicator
                GridLoadingRing.IsActive = true;
                GridImagesView.Items.Clear();
                GridPanelStatus.Text = $"Loading artworks for {game.Name ?? $"{game.Platform} / {game.XboxPlatformId}"}...";

                // Get the platform string for SteamGridDB API
                string platformString = GamePlatformHelper.GamePlatformToSGDBApiString(game.Platform);

                if (string.IsNullOrEmpty(platformString))
                {
                    GridPanelStatus.Text = "Unsupported platform";
                    GridLoadingRing.IsActive = false;
                    return;
                }

                // Find the game folder
                if (!await TrySetCurrentGameFolderAsync(game.Directory))
                {
                    GridPanelStatus.Text = "Could not access game folder";
                    GridLoadingRing.IsActive = false;

                    return;
                }

                // Fetch grids and icons from SteamGridDB
                using (var client = new SteamGridDbClient(steamGridDbApiKey))
                {
                    // Fetch both grids and icons by platform ID
                    var grids = await client.GetSquareGridsByPlatformIdAsync(platformString, game.XboxPlatformId);
                    var icons = await client.GetSquareIconsByPlatformIdAsync(platformString, game.XboxPlatformId);

                    PopulateGridSelectionPanel(grids, icons);
                }

                GridLoadingRing.IsActive = false;
            }
            catch (Exception ex)
            {
                GridPanelStatus.Text = $"Error: {ex.Message}";
                GridLoadingRing.IsActive = false;
                System.Diagnostics.Debug.WriteLine($"Error loading artworks: {ex.Message}");
            }
        }

        /// <summary>
        /// Attempts to set the current game folder for the specified directory.
        /// </summary>
        /// <param name="directory">The game directory name</param>
        /// <returns>True if successful, false otherwise</returns>
        private async Task<bool> TrySetCurrentGameFolderAsync(string directory)
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string thirdPartyLibrariesPath = Path.Combine(userProfile,
                @"AppData\Local\Packages\Microsoft.GamingApp_8wekyb3d8bbwe\LocalState\ThirdPartyLibraries");
            string gameFolderPath = Path.Combine(thirdPartyLibrariesPath, directory);

            try
            {
                currentGameFolder = await StorageFolder.GetFolderFromPathAsync(gameFolderPath);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Populates the grid selection panel with the provided grids and icons.
        /// </summary>
        /// <param name="grids">Collection of grid artworks</param>
        /// <param name="icons">Collection of icon artworks</param>
        private void PopulateGridSelectionPanel(IList<SteamGridDbGrid> grids, IList<SteamGridDbGrid> icons)
        {
            // Combine grids and icons
            var allArtworks = new List<SteamGridDbGrid>();

            if (grids != null && grids.Count > 0)
            {
                allArtworks.AddRange(grids);
            }

            if (icons != null && icons.Count > 0)
            {
                allArtworks.AddRange(icons);
            }

            if (allArtworks.Count == 0)
            {
                GridPanelStatus.Text = "No artworks found for this game";

                return;
            }

            // Sort by score (highest first)
            var sortedArtworks = allArtworks.OrderByDescending(g => g.Score).ToList();

            // Add items to grid view
            foreach (var artwork in sortedArtworks)
            {
                GridImagesView.Items.Add(new GridImageItem
                {
                    Id = artwork.Id,
                    Url = artwork.Url,
                    ThumbUrl = artwork.Thumb ?? artwork.Url,
                    Author = artwork.Author?.Name ?? unknownName,
                    Style = artwork.Style ?? "default",
                    Score = artwork.Score
                });
            }

            int gridCount = grids?.Count ?? 0;
            int iconCount = icons?.Count ?? 0;
            GridPanelStatus.Text = $"Found {gridCount} grid{(gridCount == 1 ? "" : "s")} and {iconCount} icon{(iconCount == 1 ? "" : "s")} ({allArtworks.Count} total)";
        }

        /// <summary>
        /// Handle grid image selection
        /// </summary>
        private async void GridImage_Click(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is GridImageItem gridItem && CurrentSelectedGame != null)
            {
                await DownloadAndReplaceImageAsync(gridItem);
            }
        }

        /// <summary>
        /// Download selected grid and replace the game's image file
        /// </summary>
        private async Task DownloadAndReplaceImageAsync(GridImageItem gridItem)
        {
            try
            {
                GridPanelStatus.Text = "Downloading image...";
                GridLoadingRing.IsActive = true;

                // Use the core download and replace logic
                bool success = await DownloadAndReplaceImageCoreAsync(CurrentSelectedGame, currentGameFolder, gridItem.Url, updateStatusText: true);

                if (success)
                {
                    GridPanelStatus.Text = "Image updated successfully";

                    // Close panel after short delay
                    await Task.Delay(250);
                    await HideGridPanelAsync();
                }
                else
                {
                    GridPanelStatus.Text = "Failed to download or save image";
                }

                GridLoadingRing.IsActive = false;
            }
            catch (Exception ex)
            {
                GridPanelStatus.Text = $"Error: {ex.Message}";
                GridLoadingRing.IsActive = false;
                System.Diagnostics.Debug.WriteLine($"Error downloading image: {ex.Message}");
            }
        }

        /// <summary>
        /// Show the grid selection panel with animation
        /// </summary>
        private async Task ShowGridPanelAsync()
        {
            GridSelectionPanel.Visibility = Visibility.Visible;

            // Slide up from bottom animation (like Xbox notifications)
            var animation = new DoubleAnimation
            {
                From = 800,  // Start below screen
                To = 0,      // End at normal position
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            Storyboard.SetTarget(animation, GridPanelTransform);
            Storyboard.SetTargetProperty(animation, "Y");  // Animate Y instead of X

            storyboard.Begin();
            await Task.Delay(250);
        }

        /// <summary>
        /// Hide the grid selection panel with animation
        /// </summary>
        private async Task HideGridPanelAsync()
        {
            // Slide down animation (reverse)
            var animation = new DoubleAnimation
            {
                From = 0,
                To = 800,  // Slide below screen
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            Storyboard.SetTarget(animation, GridPanelTransform);
            Storyboard.SetTargetProperty(animation, "Y");  // Animate Y instead of X

            storyboard.Begin();
            await Task.Delay(200);

            GridSelectionPanel.Visibility = Visibility.Collapsed;
            GridImagesView.Items.Clear();
            CurrentSelectedGame = null;
            currentGameFolder = null;
        }

        /// <summary>
        /// Handle close button click
        /// </summary>
        private async void CloseGridPanel_Click(object sender, RoutedEventArgs e)
        {
            await HideGridPanelAsync();
        }

        /// <summary>
        /// Handle search button click to show game search panel
        /// </summary>
        private async void SearchGameImage_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;

            if (button?.Tag is GameEntry gameEntry)
            {
                CurrentSelectedGame = gameEntry;
                await ShowSearchPanelAsync();
            }
        }

        /// <summary>
        /// Handle search box key down (Enter to search)
        /// </summary>
        private async void GameSearchBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                await PerformGameSearchAsync();
            }
        }

        /// <summary>
        /// Handle search button click
        /// </summary>
        private async void SearchGames_Click(object sender, RoutedEventArgs e)
        {
            await PerformGameSearchAsync();
        }

        /// <summary>
        /// Perform game search using SteamGridDB API
        /// </summary>
        private async Task PerformGameSearchAsync()
        {
            try
            {
                string searchTerm = GameSearchBox.Text?.Trim();

                if (string.IsNullOrEmpty(searchTerm))
                {
                    SearchPanelStatus.Text = "Please enter a game name";
                    return;
                }

                SearchLoadingRing.IsActive = true;
                SearchResultsListView.Items.Clear();
                SearchPanelStatus.Text = $"Searching for '{searchTerm}'...";

                using (var client = new SteamGridDbClient(steamGridDbApiKey))
                {
                    var results = await client.SearchGameByNameAsync(searchTerm);

                    if (results == null || results.Count == 0)
                    {
                        SearchPanelStatus.Text = "No games found";
                        SearchLoadingRing.IsActive = false;
                        return;
                    }

                    // Add results to list
                    foreach (var game in results)
                    {
                        SearchResultsListView.Items.Add(game);
                    }

                    SearchPanelStatus.Text = $"Found {results.Count} game{(results.Count == 1 ? "" : "s")}";
                }

                SearchLoadingRing.IsActive = false;
            }
            catch (Exception ex)
            {
                SearchPanelStatus.Text = $"Error: {ex.Message}";
                SearchLoadingRing.IsActive = false;
                System.Diagnostics.Debug.WriteLine($"Error searching games: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle search result selection
        /// </summary>
        private async void SearchResult_Click(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SteamGridDbGame selectedGame)
            {
                // DO NOT update current game's name - keep it as "Unknown" so user can search again

                // Hide search panel and show grid selection panel
                await HideSearchPanelAsync();
                await LoadGridSelectionByGameIdAsync(selectedGame);
            }
        }

        /// <summary>
        /// Loads grid selection panel for a game by its SteamGridDB ID.
        /// Reuses the existing LoadGridSelectionPanelAsync logic.
        /// </summary>
        private async Task LoadGridSelectionByGameIdAsync(SteamGridDbGame game)
        {
            try
            {
                // Update panel header
                GridPanelHeaderText = $"Select artwork for {game.Name} (SteamGridDB ID: {game.Id})";

                // Show panel with animation
                await ShowGridPanelAsync();

                // Show loading indicator
                GridLoadingRing.IsActive = true;
                GridImagesView.Items.Clear();
                GridPanelStatus.Text = $"Loading artworks for {game.Name}...";

                // Find the game folder (for saving later)
                if (!await TrySetCurrentGameFolderAsync(CurrentSelectedGame.Directory))
                {
                    GridPanelStatus.Text = "Could not access game folder";
                    GridLoadingRing.IsActive = false;
                    return;
                }

                // Fetch grids and icons from SteamGridDB by game ID
                using (var client = new SteamGridDbClient(steamGridDbApiKey))
                {
                    // Fetch both grids and icons by game ID
                    var grids = await client.GetSquareGridsByGameIdAsync(game.Id);
                    var icons = await client.GetSquareIconsByGameIdAsync(game.Id);

                    PopulateGridSelectionPanel(grids, icons);
                }

                GridLoadingRing.IsActive = false;
            }
            catch (Exception ex)
            {
                GridPanelStatus.Text = $"Error: {ex.Message}";
                GridLoadingRing.IsActive = false;
                System.Diagnostics.Debug.WriteLine($"Error loading artworks: {ex.Message}");
            }
        }

        /// <summary>
        /// Show the search panel with animation
        /// </summary>
        private async Task ShowSearchPanelAsync()
        {
            // Update header with game information
            if (CurrentSelectedGame != null)
            {
                if (CurrentSelectedGame.Name == unknownName)
                {
                    SearchPanelHeaderText = $"Manual search for a game from {CurrentSelectedGame.Platform}, ID: {CurrentSelectedGame.ExternalPlatformId}";
                }
                else
                {
                    SearchPanelHeaderText = $"Manual search for {CurrentSelectedGame.Name}";
                }
            }
            else
            {
                SearchPanelHeaderText = "Manual search";
            }

            GameSearchPanel.Visibility = Visibility.Visible;

            // Prefill search box with game name if it's not "Unknown"
            if (CurrentSelectedGame != null && CurrentSelectedGame.Name != unknownName)
            {
                GameSearchBox.Text = CurrentSelectedGame.Name;
            }
            else
            {
                GameSearchBox.Text = "";
            }

            SearchResultsListView.Items.Clear();
            SearchPanelStatus.Text = "Enter game name to search";

            // Slide up from bottom animation
            var animation = new DoubleAnimation
            {
                From = 800,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            Storyboard.SetTarget(animation, SearchPanelTransform);
            Storyboard.SetTargetProperty(animation, "Y");

            storyboard.Begin();
            await Task.Delay(250);

            // Focus search box
            GameSearchBox.Focus(FocusState.Programmatic);

            // Select all text if prefilled
            if (!string.IsNullOrEmpty(GameSearchBox.Text))
            {
                GameSearchBox.SelectAll();
            }
        }

        /// <summary>
        /// Hide the search panel with animation
        /// </summary>
        private async Task HideSearchPanelAsync()
        {
            // Slide down animation
            var animation = new DoubleAnimation
            {
                From = 0,
                To = 800,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            Storyboard.SetTarget(animation, SearchPanelTransform);
            Storyboard.SetTargetProperty(animation, "Y");

            storyboard.Begin();
            await Task.Delay(200);

            GameSearchPanel.Visibility = Visibility.Collapsed;
            SearchResultsListView.Items.Clear();
        }

        /// <summary>
        /// Handle close search panel button click
        /// </summary>
        private async void CloseSearchPanel_Click(object sender, RoutedEventArgs e)
        {
            await HideSearchPanelAsync();
        }

        /// <summary>
        /// Handle restore backup button click
        /// </summary>
        private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;

            if (button?.Tag is GameEntry gameEntry)
            {
                await RestoreBackupAsync(gameEntry);
            }
        }

        /// <summary>
        /// Restore image from backup file
        /// </summary>
        private async Task RestoreBackupAsync(GameEntry game)
        {
            try
            {
                string backupGameName = game.Name != unknownName ? game.Name : game.ImageFileName;

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    StatusText.Text = $"Restoring backup for {backupGameName}...";
                });

                // Find the game folder
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string thirdPartyLibrariesPath = Path.Combine(userProfile,
                  @"AppData\Local\Packages\Microsoft.GamingApp_8wekyb3d8bbwe\LocalState\ThirdPartyLibraries");
                string gameFolderPath = Path.Combine(thirdPartyLibrariesPath, game.Directory);

                StorageFolder gameFolder = null;

                try
                {
                    gameFolder = await StorageFolder.GetFolderFromPathAsync(gameFolderPath);
                }
                catch
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        StatusText.Text = $"Could not access folder for {backupGameName}";
                    });

                    return;
                }

                // Generate the filenames
                string imageFileName = $"{GamePlatformHelper.ToXboxDirectory(game.Platform)}_{game.XboxPlatformId.Replace(":", "_")}.png";
                string backupFileName = $"{GamePlatformHelper.ToXboxDirectory(game.Platform)}_{game.XboxPlatformId.Replace(":", "_")}.bak";

                // Delete current image if it exists
                try
                {
                    var currentImageFile = await gameFolder.GetFileAsync(imageFileName);
                    await currentImageFile.DeleteAsync();
                }
                catch (FileNotFoundException)
                {
                    // Current image doesn't exist, that's okay
                }

                // Rename backup to become the main image
                try
                {
                    var backupFile = await gameFolder.GetFileAsync(backupFileName);
                    await backupFile.RenameAsync(imageFileName, NameCollisionOption.ReplaceExisting);

                    // Reload the image in the UI - open stream before dispatching to UI thread
                    var imageFile = await gameFolder.GetFileAsync(imageFileName);
                    var stream = await imageFile.OpenReadAsync();

                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        try
                        {
                            var restoredImage = new BitmapImage();
                            // Fire-and-forget: async call will complete in background
                            var _ = restoredImage.SetSourceAsync(stream);

                            game.Image = restoredImage;
                            game.ImageFileName = imageFileName;
                            game.HasBackup = false; // Backup no longer exists

                            StatusText.Text = $"Backup restored for {backupGameName}";
                        }
                        catch
                        {
                            stream?.Dispose();
                        }
                    });
                }
                catch (FileNotFoundException)
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        StatusText.Text = $"Backup file not found for {backupGameName}";
                    });
                }
                catch (Exception ex)
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        StatusText.Text = $"Error restoring backup for {backupGameName}: {ex.Message}";
                    });

                    System.Diagnostics.Debug.WriteLine($"Error restoring backup for {backupGameName}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    StatusText.Text = $"Error restoring backup: {ex.Message}";
                });

                System.Diagnostics.Debug.WriteLine($"Error in RestoreBackupAsync: {ex.Message}");
            }
        }

        /// <summary>
        /// Fetches game name from GOG API by GOG ID.
        /// </summary>
        /// <param name="gogId">The GOG game ID</param>
        /// <returns>Game name or null if not found</returns>
        private async Task<string> GetGogGameNameAsync(string gogId)
        {
            try
            {
                var url = $"https://api.gog.com/v2/games/{gogId}";
                var response = await sharedHttpClient.GetAsync(new Uri(url));

                if (response.IsSuccessStatusCode)
                {
                    var jsonContent = await response.Content.ReadAsStringAsync();

                    if (JsonObject.TryParse(jsonContent, out JsonObject gameData))
                    {
                        if (gameData.ContainsKey("_embedded") &&
                            gameData.GetNamedObject("_embedded").ContainsKey("product"))
                        {
                            var product = gameData.GetNamedObject("_embedded").GetNamedObject("product");

                            if (product.ContainsKey("title"))
                            {
                                return product.GetNamedString("title");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching GOG game name for {gogId}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Fetches Epic Games Store game name from GitHub by external platform ID.
        /// </summary>
        /// <param name="epicId">The Epic Games Store ID.</param>
        /// <returns>Game name or null if not found.</returns>
        private async Task<string> GetEpicGameNameAsync(string epicId)
        {
            try
            {
                var url = $"https://raw.githubusercontent.com/nachoaldamav/items-tracker/refs/heads/main/database/items/{epicId}.json";
                var response = await sharedHttpClient.GetAsync(new Uri(url));

                if (response.IsSuccessStatusCode)
                {
                    var jsonContent = await response.Content.ReadAsStringAsync();

                    if (JsonObject.TryParse(jsonContent, out JsonObject gameData))
                    {
                        if (gameData.ContainsKey("title"))
                        {
                            return gameData.GetNamedString("title");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching Epic game name for {epicId}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Downloads and parses the Ubisoft game list from GitHub.
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        private async Task<bool> LoadUbisoftGameListAsync()
        {
            if (ubisoftGameLookupCache != null)
            {
                return true;
            }

            try
            {
                var url = "https://raw.githubusercontent.com/Haoose/UPLAY_GAME_ID/refs/heads/master/README.md";
                var response = await sharedHttpClient.GetAsync(new Uri(url));

                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                var content = await response.Content.ReadAsStringAsync();
                var lines = content.Split('\n');

                ubisoftGameLookupCache = new Dictionary<string, string>();

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();

                    if (string.IsNullOrEmpty(trimmedLine))
                    {
                        continue;
                    }

                    // Format: "232 - Beyond Good and Evil™"
                    var dashIndex = trimmedLine.IndexOf(" - ");

                    if (dashIndex > 0)
                    {
                        var idPart = trimmedLine.Substring(0, dashIndex).Trim();
                        var namePart = trimmedLine.Substring(dashIndex + 3).Trim();

                        if (!string.IsNullOrEmpty(idPart) && !string.IsNullOrEmpty(namePart))
                        {
                            ubisoftGameLookupCache[idPart] = namePart;
                        }
                    }
                }

                return ubisoftGameLookupCache.Count > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading Ubisoft game list: {ex.Message}");

                return false;
            }
        }

        /// <summary>
        /// Fetches game name from cached Ubisoft game list by Ubisoft ID.
        /// </summary>
        /// <param name="ubisoftId">The Ubisoft game ID</param>
        /// <returns>Game name or null if not found</returns>
        private async Task<string> GetUbisoftGameNameAsync(string ubisoftId)
        {
            try
            {
                await LoadUbisoftGameListAsync();

                if (ubisoftGameLookupCache != null && ubisoftGameLookupCache.TryGetValue(ubisoftId, out string gameName))
                {
                    return gameName;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching Ubisoft game name for {ubisoftId}: {ex.Message}");
            }

            return null;
        }
    }
}
