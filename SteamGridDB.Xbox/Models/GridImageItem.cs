using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SteamGridDB.Xbox.Models
{
    /// <summary>
    /// Represents a grid image item for display in the selection panel.
    /// </summary>
    public class GridImageItem : INotifyPropertyChanged
    {
        private string url;
        private string thumbUrl;
        private string style;
        private string author;
        private int score;
        private int id;

        public string Url
        {
            get => url;
            set
            {
                if (url != value)
                {
                    url = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ThumbUrl
        {
            get => thumbUrl;
            set
            {
                if (thumbUrl != value)
                {
                    thumbUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Style
        {
            get => style;
            set
            {
                if (style != value)
                {
                    style = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Author
        {
            get => author;
            set
            {
                if (author != value)
                {
                    author = value;
                    OnPropertyChanged();
                }
            }
        }

        public int Score
        {
            get => score;
            set
            {
                if (score != value)
                {
                    score = value;
                    OnPropertyChanged();
                }
            }
        }

        public int Id
        {
            get => id;
            set
            {
                if (id != value)
                {
                    id = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
