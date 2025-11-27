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
        private string xboxPlatformId;
        private string externalPlatformId;
        private string directory;
        private GamePlatform platform;
        private DateTime addedDate;
        private string imageFileName;
        private BitmapImage image;
        private bool hasBackup;
        private bool hasSteamGridDBMatch;

        public string Name
        {
            get => name;
            set
            {
                if (name != value)
                {
                    name = value;
                    OnPropertyChanged();
                }
            }
        }

        public string XboxPlatformId
        {
            get => xboxPlatformId;
            set
            {
                if (xboxPlatformId != value)
                {
                    xboxPlatformId = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ExternalPlatformId
        {
            get => externalPlatformId;
            set
            {
                if (externalPlatformId != value)
                {
                    externalPlatformId = value;
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
                }
            }
        }

        public bool HasSteamGridDBMatch
        {
            get => hasSteamGridDBMatch;
            set
            {
                if (hasSteamGridDBMatch != value)
                {
                    hasSteamGridDBMatch = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EditButtonVisibility));
                    OnPropertyChanged(nameof(SearchButtonVisibility));
                }
            }
        }

        // Edit button visible when there is a match
        public Visibility EditButtonVisibility => HasSteamGridDBMatch ? Visibility.Visible : Visibility.Collapsed;

        // Search button visible when there is no match
        public Visibility SearchButtonVisibility => !HasSteamGridDBMatch ? Visibility.Visible : Visibility.Collapsed;

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
