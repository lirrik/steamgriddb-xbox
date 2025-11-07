using System;
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
using Windows.Foundation.Metadata;

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
        private int currentSearchedGameId = 0;

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
                    StatusText.Text = $"Found {folders.Count} directories. Loading...";
                });

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
                                string platformId = entryId.Substring(entryId.IndexOf(':') + 1);
                                GamePlatform platform = GamePlatformHelper.FromXboxDirectory(directoryName);

                                if (!string.IsNullOrEmpty(steamGridDbApiKey))
                                {
                                    try
                                    {
                                        string platformString = GamePlatformHelper.GamePlatformToSGDBApiString(platform);

                                        if (!string.IsNullOrEmpty(platformString))
                                        {
                                            using (var client = new SteamGridDbClient(steamGridDbApiKey))
                                            {
                                                var gameInfo = await client.GetGameByPlatformIdAsync(platformString, platformId);

                                                if (gameInfo != null && !string.IsNullOrEmpty(gameInfo.Name))
                                                {
                                                    gameName = gameInfo.Name;
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


                                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                                {
                                    GameEntries.Add(new GameEntry
                                    {
                                        Name = gameName,
                                        PlatformId = platformId,
                                        ImageFileName = imageName,
                                        Platform = platform,
                                        AddedDate = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime,
                                        Directory = directoryName,
                                        Image = image,
                                        HasBackup = hasBackup
                                    });
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

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    StatusText.Text = $"Loaded {GameEntries.Count} game entries";
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
                GridPanelHeaderText = $"Select artwork for {game.Name} (platform: {game.Platform}, ID: {game.PlatformId})";

                // Show panel with animation
                await ShowGridPanelAsync();

                // Show loading indicator
                GridLoadingRing.IsActive = true;
                GridImagesView.Items.Clear();
                GridPanelStatus.Text = $"Loading grids for {game.ImageFileName ?? game.PlatformId}...";

                // Get the platform string for SteamGridDB API
                string platformString = GamePlatformHelper.GamePlatformToSGDBApiString(game.Platform);

                if (string.IsNullOrEmpty(platformString))
                {
                    GridPanelStatus.Text = "Unsupported platform";
                    GridLoadingRing.IsActive = false;
                    return;
                }

                // Find the game folder
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string thirdPartyLibrariesPath = Path.Combine(userProfile,
                   @"AppData\Local\Packages\Microsoft.GamingApp_8wekyb3d8bbwe\LocalState\ThirdPartyLibraries");
                string gameFolderPath = Path.Combine(thirdPartyLibrariesPath, game.Directory);

                try
                {
                    currentGameFolder = await StorageFolder.GetFolderFromPathAsync(gameFolderPath);
                }
                catch
                {
                    GridPanelStatus.Text = "Could not access game folder";
                    GridLoadingRing.IsActive = false;
                    return;
                }

                // Fetch grids from SteamGridDB
                using (var client = new SteamGridDbClient(steamGridDbApiKey))
                {
                    var grids = await client.GetSquareGridsByPlatformIdAsync(platformString, game.PlatformId);

                    if (grids == null || grids.Count == 0)
                    {
                        GridPanelStatus.Text = "No artworks found for this game";
                        GridLoadingRing.IsActive = false;

                        return;
                    }

                    // Sort by score (highest first)
                    var sortedGrids = grids.OrderByDescending(g => g.Score).ToList();

                    // Add items to grid view
                    foreach (var grid in sortedGrids)
                    {
                        GridImagesView.Items.Add(new GridImageItem
                        {
                            Id = grid.Id,
                            Url = grid.Url,
                            ThumbUrl = grid.Thumb ?? grid.Url,
                            Author = grid.Author?.Name ?? "Unknown",
                            Style = grid.Style ?? "default",
                            Score = grid.Score
                        });
                    }

                    GridPanelStatus.Text = $"Found {grids.Count} artworks(s)";
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

                // Download the image
                using (var httpClient = new HttpClient())
                {
                    var response = await httpClient.GetAsync(new Uri(gridItem.Url));

                    if (!response.IsSuccessStatusCode)
                    {
                        GridPanelStatus.Text = "Failed to download image";
                        GridLoadingRing.IsActive = false;

                        return;
                    }

                    var imageBytes = await response.Content.ReadAsBufferAsync();

                    // Generate the filenames
                    string imageFileName = $"{GamePlatformHelper.ToXboxDirectory(currentSelectedGame.Platform)}_{currentSelectedGame.PlatformId}.png";
                    string backupFileName = $"{GamePlatformHelper.ToXboxDirectory(currentSelectedGame.Platform)}_{currentSelectedGame.PlatformId}.bak";

                    // Create backup of ORIGINAL image ONLY if backup doesn't already exist
                    bool backupExists = false;

                    try
                    {
                        await currentGameFolder.GetFileAsync(backupFileName);
                        backupExists = true;
                        GridPanelStatus.Text = "Original backup exists, downloading new image...";
                    }
                    catch (FileNotFoundException)
                    {
                        // Backup doesn't exist, create it from current image
                        try
                        {
                            var existingImageFile = await currentGameFolder.GetFileAsync(imageFileName);

                            // Backup the ORIGINAL image by copying (not renaming) to preserve it
                            var backupFile = await currentGameFolder.CreateFileAsync(backupFileName, CreationCollisionOption.ReplaceExisting);
                            backupExists = true;
                            var existingBuffer = await FileIO.ReadBufferAsync(existingImageFile);
                            await FileIO.WriteBufferAsync(backupFile, existingBuffer);

                            GridPanelStatus.Text = "Original backed up, downloading new image...";
                        }
                        catch (FileNotFoundException)
                        {
                            // No existing image to backup
                            GridPanelStatus.Text = "Downloading new image...";
                        }
                    }

                    // Save the new image (replaces current)
                    try
                    {
                        var imageFile = await currentGameFolder.CreateFileAsync(imageFileName, CreationCollisionOption.ReplaceExisting);
                        await FileIO.WriteBufferAsync(imageFile, imageBytes);

                        //// Set the file to read-only to prevent external overwrites - looks like this is not actually needed?
                        //try
                        //{
                        //    // Set read-only attribute using Win32 file system
                        //    string fullPath = imageFile.Path;
                        //    System.IO.FileAttributes currentAttributes = System.IO.File.GetAttributes(fullPath);
                        //    System.IO.File.SetAttributes(fullPath, currentAttributes | System.IO.FileAttributes.ReadOnly);

                        //    System.Diagnostics.Debug.WriteLine($"Set {imageFileName} to read-only");
                        //}
                        //catch (Exception attrEx)
                        //{
                        //    // Log but don't fail if we can't set read-only
                        //    System.Diagnostics.Debug.WriteLine($"Could not set read-only attribute: {attrEx.Message}");
                        //}

                        // Reload the image in the UI
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                        {
                            var newImage = new BitmapImage();

                            using (var stream = await imageFile.OpenReadAsync())
                            {
                                await newImage.SetSourceAsync(stream);
                            }

                            currentSelectedGame.Image = newImage;
                            currentSelectedGame.ImageFileName = imageFileName;
                            currentSelectedGame.HasBackup = backupExists; // Mark that backup exists (original preserved)

                            StatusText.Text = $"Image {imageFileName} updated successfully";
                        });

                        GridPanelStatus.Text = "Image updated successfully";

                        // Close panel after short delay
                        await Task.Delay(1000);
                        await HideGridPanelAsync();
                    }
                    catch (Exception ex)
                    {
                        GridPanelStatus.Text = $"Error saving image: {ex.Message}";
                        System.Diagnostics.Debug.WriteLine($"Error saving image: {ex.Message}");
                    }
                }
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

                    SearchPanelStatus.Text = $"Found {results.Count} game(s)";
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
                // Store the selected game ID
                currentSearchedGameId = selectedGame.Id;

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
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string thirdPartyLibrariesPath = Path.Combine(userProfile,
                     @"AppData\Local\Packages\Microsoft.GamingApp_8wekyb3d8bbwe\LocalState\ThirdPartyLibraries");
                string gameFolderPath = Path.Combine(thirdPartyLibrariesPath, currentSelectedGame.Directory);

                try
                {
                    currentGameFolder = await StorageFolder.GetFolderFromPathAsync(gameFolderPath);
                }
                catch
                {
                    GridPanelStatus.Text = "Could not access game folder";
                    GridLoadingRing.IsActive = false;

                    return;
                }

                // Fetch grids from SteamGridDB by game ID
                using (var client = new SteamGridDbClient(steamGridDbApiKey))
                {
                    var grids = await client.GetSquareGridsByGameIdAsync(game.Id);

                    if (grids == null || grids.Count == 0)
                    {
                        GridPanelStatus.Text = "No artworks found for this game";
                        GridLoadingRing.IsActive = false;
                        return;
                    }

                    // Sort by score (highest first)
                    var sortedGrids = grids.OrderByDescending(g => g.Score).ToList();

                    // Add items to grid view
                    foreach (var grid in sortedGrids)
                    {
                        GridImagesView.Items.Add(new GridImageItem
                        {
                            Id = grid.Id,
                            Url = grid.Url,
                            ThumbUrl = grid.Thumb ?? grid.Url,
                            Author = grid.Author?.Name ?? "Unknown",
                            Style = grid.Style ?? "default",
                            Score = grid.Score
                        });
                    }

                    GridPanelStatus.Text = $"Found {grids.Count} artwork(s)";
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
                    StatusText.Text = $"Restoring backup for {game.ImageFileName ?? game.PlatformId}...";
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
                string imageFileName = $"{GamePlatformHelper.ToXboxDirectory(game.Platform)}_{game.PlatformId}.png";
                string backupFileName = $"{GamePlatformHelper.ToXboxDirectory(game.Platform)}_{game.PlatformId}.bak";

                //// Remove read-only attribute from current image if it exists - looks like this is not actually needed?
                //try
                //{
                //    string currentImagePath = Path.Combine(gameFolder.Path, imageFileName);
                //    if (System.IO.File.Exists(currentImagePath))
                //    {
                //        System.IO.FileAttributes currentAttributes = System.IO.File.GetAttributes(currentImagePath);
                //        if ((currentAttributes & System.IO.FileAttributes.ReadOnly) == System.IO.FileAttributes.ReadOnly)
                //        {
                //            // Remove read-only flag
                //            System.IO.File.SetAttributes(currentImagePath, currentAttributes & ~System.IO.FileAttributes.ReadOnly);
                //            System.Diagnostics.Debug.WriteLine($"Removed read-only from {imageFileName}");
                //        }
                //    }
                //}
                //catch (Exception attrEx)
                //{
                //    System.Diagnostics.Debug.WriteLine($"Could not remove read-only attribute: {attrEx.Message}");
                //}

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

                        StatusText.Text = $"Backup restored for {game.ImageFileName ?? game.PlatformId}";
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
    }
}
