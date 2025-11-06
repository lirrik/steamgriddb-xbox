using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

using SteamGridDB.Xbox.Models;

namespace SteamGridDB.Xbox
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class PrimaryWidget : Page
    {
        public ObservableCollection<GameEntry> GameEntries
        {
            get; set;
        }

        public PrimaryWidget()
        {
            this.InitializeComponent();
            GameEntries = new ObservableCollection<GameEntry>();
            this.Loaded += PrimaryWidget_Loaded;
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
                    string manifestFileName = $"{directoryName}.manifest";

                    try
                    {
                        // Try to get the manifest file
                        StorageFile manifestFile = await folder.GetFileAsync(manifestFileName);

                        // Read and parse the manifest JSON file
                        string jsonContent = await FileIO.ReadTextAsync(manifestFile);

                        JsonObject root = null;

                        if (JsonObject.TryParse(jsonContent, out root))
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
                                double timestamp = entryObject.GetNamedNumber("addedDate", 0);

                                // Convert ID to image filename (replace : with _)
                                string imageFileName = entryId.Replace(":", "_") + ".png";
                                string entryTitle = imageFileName;

                                BitmapImage image = null;

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
                                    entryTitle = null;
                                }

                                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                                {
                                    GameEntries.Add(new GameEntry
                                    {
                                        Id = entryId,
                                        ImageName = entryTitle,
                                        Platform = GamePlatformHelper.FromXboxDirectory(directoryName),
                                        AddedDate = DateTimeOffset.FromUnixTimeMilliseconds((long)timestamp).LocalDateTime,
                                        Directory = directoryName,
                                        Image = image
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
                    StatusText.Text = $"Loaded {GameEntries.Count} game entries.";
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
    }
}
