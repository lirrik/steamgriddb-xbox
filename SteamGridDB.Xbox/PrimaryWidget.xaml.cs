using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Windows.Data.Json;
using Windows.Storage;
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
    /// Primary widget page that loads and displays Xbox app third-party games.
    /// </summary>
    public sealed partial class PrimaryWidget : Page, INotifyPropertyChanged
    {
        public ObservableCollection<GameEntry> GameEntries
        {
            get; set;
        }

        private GameEntry currentSelectedGame;
        private StorageFolder currentGameFolder;

        private readonly string steamGridDbApiKey = Environment.GetEnvironmentVariable("STEAMGRIDDB_API_KEY");

        private static Dictionary<string, string> ubisoftGameLookupCache = null;

        private string gridPanelHeaderText = "Select artwork";
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
                        StatusText.Text = "ThirdPartyLibraries folder not found. Make sure games are added to Xbox app.";
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
                    StatusText.Text = $"Found {folders.Count} director{(folders.Count == 1 ? "y" : "ies")}. Loading...";
                });

                // Temporary list to collect games before sorting
                var tempGameList = new List<GameEntry>();

                foreach (var folder in folders)
                {
                    string directoryName = folder.Name;

                    if (directoryName == "bnet")
                    {
                        // Skip Battle.net folder as it is not currently supported - Xbox app does not store images here
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

                                try
                                {
                                    StorageFile imageFile = await folder.GetFileAsync(imageFileName);
                                    image = new BitmapImage();
                                    var stream = await imageFile.OpenReadAsync();
                                    await image.SetSourceAsync(stream);
                                }
                                catch (FileNotFoundException)
                                {
                                    // Image doesn't exist, that's okay
                                    imageName = "Not found";
                                }

                                // Try to fetch game name from SteamGridDB API
                                string gameName = "Unknown";
                                GamePlatform platform = GamePlatformHelper.FromXboxDirectory(directoryName);
                                string xboxPlatformId = entryId.Substring(entryId.IndexOf(':') + 1);
                                string externalPlatformId = xboxPlatformId;

                                if (platform == GamePlatform.Epic)
                                {
                                    // For Epic, entryId format is "epic:namespace:somethingElse"
                                    // Namespace is what we are after as common external ID
                                    var parts = entryId.Split(':');

                                    if (parts.Length >= 3)
                                    {
                                        externalPlatformId = parts[1];
                                    }
                                }

                                bool hasSteamGridDBMatch = false;

                                if (!string.IsNullOrEmpty(steamGridDbApiKey))
                                {
                                    try
                                    {
                                        string platformString = GamePlatformHelper.GamePlatformToSGDBApiString(platform);

                                        if (!string.IsNullOrEmpty(platformString))
                                        {
                                            using (var client = new SteamGridDbClient(steamGridDbApiKey))
                                            {
                                                var gameInfo = await client.GetGameByPlatformIdAsync(platformString, externalPlatformId);

                                                if (gameInfo != null && !string.IsNullOrEmpty(gameInfo.Name))
                                                {
                                                    gameName = gameInfo.Name;
                                                    hasSteamGridDBMatch = true;
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        // Log but don't fail - game name is optional
                                        System.Diagnostics.Debug.WriteLine($"Could not fetch game name for {entryId}: {ex.Message}");
                                    }
                                }

                                if (!hasSteamGridDBMatch)
                                {
                                    if (platform == GamePlatform.GOG)
                                    {
                                        var gogName = await GetGogGameNameAsync(externalPlatformId);

                                        if (!string.IsNullOrEmpty(gogName))
                                        {
                                            gameName = gogName;
                                        }
                                    }
                                    else if (platform == GamePlatform.Epic)
                                    {
                                        var epicName = await GetEpicGameNameAsync(externalPlatformId);

                                        if (!string.IsNullOrEmpty(epicName))
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
                                        // TODO: Implement EA app name fetching if possible
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

                // Sort games alphabetically by name, with "Unknown" at the end
                var sortedGames = tempGameList
                    .OrderBy(g => g.Name == "Unknown" ? 1 : 0)
                    .ThenBy(g => g.Name)
                    .ToList();

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    foreach (var game in sortedGames)
                    {
                        GameEntries.Add(game);
                    }

                    StatusText.Text = $"Loaded {GameEntries.Count} game entr{(GameEntries.Count == 1 ? "y" : "ies")}";
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
                Content = "This will automatically download the highest-scored artwork from SteamGridDB for all known games that haven't been manually modified yet.\n\n" +
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
        /// Automatically downloads the highest-scored artwork for games with known names and no backup
        /// </summary>
        private async Task FixLibraryAsync()
        {
            try
            {
                // Get eligible games: known name and no backup
                var eligibleGames = GameEntries.Where(g => g.Name != "Unknown" && !g.HasBackup).ToList();

                if (eligibleGames.Count == 0)
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        StatusText.Text = "No eligible game artworks to fix (all games either have unknown names or already have backups)";
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
                        using (var client = new SteamGridDbClient(steamGridDbApiKey))
                        {
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
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        System.Diagnostics.Debug.WriteLine($"Error processing {game.Name}: {ex.Message}");
                    }
                }

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    StatusText.Text = $"Fix library completed: {successCount} updated, {skipCount} skipped, {errorCount} error{(errorCount == 1 ? "" : "s")}";
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

                    // Reload the image in the UI
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                    {
                        var newImage = new BitmapImage();

                        using (var stream = await imageFile.OpenReadAsync())
                        {
                            await newImage.SetSourceAsync(stream);
                        }

                        game.Image = newImage;
                        game.ImageFileName = imageFileName;
                        game.HasBackup = backupExists;

                        if (updateStatusText)
                        {
                            StatusText.Text = $"Image {imageFileName} updated successfully";
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
                currentSelectedGame = gameEntry;

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
                    Author = artwork.Author?.Name ?? "Unknown",
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
            if (e.ClickedItem is GridImageItem gridItem && currentSelectedGame != null)
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
                bool success = await DownloadAndReplaceImageCoreAsync(currentSelectedGame, currentGameFolder, gridItem.Url, updateStatusText: true);

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
            currentSelectedGame = null;
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
                currentSelectedGame = gameEntry;
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
                if (!await TrySetCurrentGameFolderAsync(currentSelectedGame.Directory))
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
            GameSearchPanel.Visibility = Visibility.Visible;
            GameSearchBox.Text = "";
            SearchResultsListView.Items.Clear();
            SearchPanelStatus.Text = "Enter a game name to search";

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
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    StatusText.Text = $"Restoring backup for {game.ImageFileName ?? game.XboxPlatformId}...";
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
                        StatusText.Text = "Could not access game folder";
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

                    // Reload the image in the UI
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                    {
                        var restoredImage = new BitmapImage();
                        var imageFile = await gameFolder.GetFileAsync(imageFileName);

                        using (var stream = await imageFile.OpenReadAsync())
                        {
                            await restoredImage.SetSourceAsync(stream);
                        }

                        game.Image = restoredImage;
                        game.ImageFileName = imageFileName;
                        game.HasBackup = false; // Backup no longer exists

                        StatusText.Text = $"Backup restored for {game.ImageFileName ?? game.XboxPlatformId}";
                    });
                }
                catch (FileNotFoundException)
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        StatusText.Text = "Backup file not found";
                    });
                }
                catch (Exception ex)
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        StatusText.Text = $"Error restoring backup: {ex.Message}";
                    });

                    System.Diagnostics.Debug.WriteLine($"Error restoring backup: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    StatusText.Text = $"Error: {ex.Message}";
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
                using (var httpClient = new HttpClient())
                {
                    var url = $"https://api.gog.com/v2/games/{gogId}";
                    var response = await httpClient.GetAsync(new Uri(url));

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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching GOG game name for {gogId}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Fetches game name from Epic Games Store GraphQL API by namespace.
        /// </summary>
        /// <param name="epicNamespace">The Epic Games namespace.</param>
        /// <returns>Game name or null if not found.</returns>
        private async Task<string> GetEpicGameNameAsync(string epicNamespace)
        {
            try
            {
                // GraphQL query to get catalog offers by namespace
                string query = @"
                    query catalogQuery($namespace: String!) {
                        Catalog {
                            catalogOffers(namespace: $namespace, locale: ""en-US"") {
                                elements {
                                    title
                                }
                            }
                        }
                    }";

                // Build JSON request body
                string requestJson = $@"{{
                    ""query"": ""{query.Replace("\r", "").Replace("\n", " ").Replace("\"", "\\\"")}"",
                    ""variables"": {{
                        ""namespace"": ""{epicNamespace}"",
                    }}
                }}";

                using (var httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "SteamGridDB.Xbox/1.2");

                    // Create POST request content
                    var content = new HttpStringContent(
                        requestJson,
                        Windows.Storage.Streams.UnicodeEncoding.Utf8,
                        "application/json"
                    );

                    var response = await httpClient.PostAsync(new Uri("https://store.epicgames.com/graphql"), content);

                    // Most of the time this will return 403 because of CAPTCHA - not sure how to bypass it
                    if (response.IsSuccessStatusCode)
                    {
                        string responseJson = await response.Content.ReadAsStringAsync();

                        if (JsonObject.TryParse(responseJson, out JsonObject responseData))
                        {
                            // Navigate through JSON: data -> Catalog -> catalogOffers -> elements
                            if (responseData.ContainsKey("data"))
                            {
                                var data = responseData.GetNamedObject("data");

                                if (data.ContainsKey("Catalog"))
                                {
                                    var catalog = data.GetNamedObject("Catalog");

                                    if (catalog.ContainsKey("catalogOffers"))
                                    {
                                        var catalogOffers = catalog.GetNamedObject("catalogOffers");

                                        if (catalogOffers.ContainsKey("elements"))
                                        {
                                            var elements = catalogOffers.GetNamedArray("elements");

                                            // Get the first element (game)
                                            if (elements.Count > 0)
                                            {
                                                var firstElement = elements.GetObjectAt(0);

                                                if (firstElement.ContainsKey("title"))
                                                {
                                                    return firstElement.GetNamedString("title");
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching Epic game name for {epicNamespace}: {ex.Message}");
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
                using (var httpClient = new HttpClient())
                {
                    var url = "https://raw.githubusercontent.com/Haoose/UPLAY_GAME_ID/refs/heads/master/README.md";
                    var response = await httpClient.GetAsync(new Uri(url));

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
