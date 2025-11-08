using Windows.UI.Xaml.Controls;

namespace SteamGridDB.Xbox
{
    /// <summary>
    /// Page that opens if the widget app is launched as a regular UWP app instead of Xbox Game Bar widget (for example, from the Start menu or when debugging).
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            InitializeComponent();
        }
    }
}
