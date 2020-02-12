using System;
using Xamarin.Forms;

namespace Heleus.Apps.Shared.iOS
{
    public partial class AppDelegate
    {
        static Color TintColor => Theme.PrimaryColor.Color.AddLuminosity(0.25);
    }
}
