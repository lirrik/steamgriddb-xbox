using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;

namespace SteamGridDB.Xbox.Models
{
    public class GameEntry : INotifyPropertyChanged
    {
        private string name;
        private string platformId;
        private string directory;
        private GamePlatform platform;
        private DateTime addedDate;
        private string imageFileName;
        private BitmapImage image;
        private bool hasBackup;

        public string Name
        {
            get => name;
            set
            {
                if (name != value)
                {
                    name = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanEditImage));
                    OnPropertyChanged(nameof(CanSearchGame));
                    OnPropertyChanged(nameof(EditButtonVisibility));
                    OnPropertyChanged(nameof(SearchButtonVisibility));
                }
            }
        }

        public string PlatformId
        {
            get => platformId;
            set
            {
                if (platformId != value)
                {
                    platformId = value;
                    OnPropertyChanged();
                }
            }
        }
        public string Directory
        {
            get => directory;
            set
            {
                if (directory != value)
                {
                    directory = value;
                    OnPropertyChanged();
                }
            }
        }

        public GamePlatform Platform
        {
            get => platform;
            set
            {
                if (platform != value)
                {
                    platform = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime AddedDate
        {
            get => addedDate;
            set
            {
                if (addedDate != value)
                {
                    addedDate = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AddedDateFormatted));
                }
            }
        }

        public string AddedDateFormatted => AddedDate.ToString();

        public string ImageFileName
        {
            get => imageFileName;
            set
            {
                if (imageFileName != value)
                {
                    imageFileName = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanEditImage));
                }
            }
        }

        // Edit button should be enabled only if ImageFileName is not "Not found"
        public bool CanEditImage => !string.IsNullOrEmpty(ImageFileName) && ImageFileName != "Not found";

        // Search button should be visible when Name is "Unknown"
        public bool CanSearchGame => Name == "Unknown";

        // Edit button visible when NOT searching
        public Visibility EditButtonVisibility => !CanSearchGame ? Visibility.Visible : Visibility.Collapsed;

        // Search button visible when Name is "Unknown"
        public Visibility SearchButtonVisibility => CanSearchGame ? Visibility.Visible : Visibility.Collapsed;

        public bool HasBackup
        {
            get => hasBackup;
            set
            {
                if (hasBackup != value)
                {
                    hasBackup = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RestoreButtonVisibility));
                }
            }
        }

        public Visibility RestoreButtonVisibility => HasBackup ? Visibility.Visible : Visibility.Collapsed;

        public BitmapImage Image
        {
            get => image;
            set
            {
                if (image != value)
                {
                    image = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasImage));
                    OnPropertyChanged(nameof(ImageVisibility));
                    OnPropertyChanged(nameof(PlaceholderVisibility));
                }
            }
        }

        public bool HasImage => Image != null;
        public Visibility ImageVisibility => Image != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PlaceholderVisibility => Image == null ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
