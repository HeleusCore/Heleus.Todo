using System;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace Heleus.Apps.Shared
{
    class TestPage : StackPage
	{
		public static bool ShowTestPage = false;

        async Task Start(ButtonRow button)
		{
			await Task.Delay (0);
		}

        async Task Close(ButtonRow button)
        {
            await UIApp.Current.PopModal(this);
        }

		public TestPage() : base("TestPage")
		{
			if (UIApp.IsUWP)
				NavigationPage.SetHasNavigationBar(this, false);

			var closeItem = new ExtToolbarItem("", "icons/ic_close.png", async () =>
			{
				await Close(null);
			});

			ToolbarItems.Add(closeItem);

			AddHeaderRow();
			AddButtonRow("Start", Start);
			AddButtonRow("Close", Close, true);
			AddFooterRow();

			Device.BeginInvokeOnMainThread(async () =>
			{
				await Task.Delay(250);
				await Start(null);
			});
		}
	}
}

