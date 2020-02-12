using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Networking.PushNotifications;
using Windows.Storage;
using Windows.UI.Notifications;

namespace BackgroundNotificationTask
{
    public sealed class Task : IBackgroundTask
    {
        public void Run(IBackgroundTaskInstance taskInstance)
        {
            var notification = (RawNotification)taskInstance.TriggerDetails;

            var notificationXml = new Windows.Data.Xml.Dom.XmlDocument();
            notificationXml.LoadXml(notification.Content);

            var root = notificationXml.FirstChild;

            var task = string.Empty;
            var payload = string.Empty;
            foreach (var attribute in root.Attributes)
            {
                if (attribute.NodeName == "task")
                    task = attribute.NodeValue.ToString();
                if (attribute.NodeName == "payload")
                    payload = attribute.NodeValue.ToString();
            }

            if (task == "message") // push.PushMessageTasks.Message
            {
                var title = string.Empty;
                var message = string.Empty;
                var logo = string.Empty;

                var disableToasts = false;
                var noTickerLogo = false;

                var x = notificationXml.ToString();

                var titleTag = notificationXml.GetElementsByTagName("title").FirstOrDefault();
                if (titleTag != null)
                    title = titleTag.InnerText;

                var messageTag = notificationXml.GetElementsByTagName("message").FirstOrDefault();
                if (messageTag != null)
                    message = messageTag.InnerText;

                if (string.IsNullOrEmpty(message))
                    return;

                var logoTag = notificationXml.GetElementsByTagName("logo").FirstOrDefault();
                if (logoTag != null)
                    logo = logoTag.InnerText;

                var settings = ApplicationData.Current.LocalSettings.Values;
                try
                {
                    object data = null;
                    if (settings.TryGetValue("DisableToast", out data))
                    {
                        disableToasts = (bool)data;
                    }
                    if (settings.TryGetValue("ToastNoLogo", out data))
                    {
                        noTickerLogo = (bool)data;
                    }
                }
                catch { }

                // toast
                if (!disableToasts)
                {
                    var toastXml = new Windows.Data.Xml.Dom.XmlDocument();
                    toastXml.LoadXml(@"<?xml version=""1.0"" encoding=""UTF-8""?>
                        <toast launch="""">
                            <visual>
                                <binding template=""ToastGeneric"">
                                    <text tf=""title""></text>
                                    <text tf=""message""></text>
       								<image placement=""appLogoOverride"" src=""""></image>
                                </binding>  
                            </visual>
                        </toast>");

                    foreach(var attribute in toastXml.ChildNodes[1].Attributes)
                    {
                        if (attribute.NodeName == "launch")
                            attribute.NodeValue = string.Format("{0}|{1}", task, payload);
                    }

                    var toastTexts = toastXml.GetElementsByTagName("text");
                    foreach (var tileText in toastTexts)
                    {
                        foreach (var attribute in tileText.Attributes)
                        {
                            if (attribute.NodeName == "tf" && attribute.NodeValue.ToString() == "title")
                                tileText.InnerText = title;
                            else if (attribute.NodeName == "tf" && attribute.NodeValue.ToString() == "message")
                                tileText.InnerText = message;
                        }
                    }

                    try
                    {
                        var image = toastXml.GetElementsByTagName("image").FirstOrDefault();
                        if (image != null)
                        {
                            if (noTickerLogo || string.IsNullOrEmpty(logo))
                            {
                                image.ParentNode.RemoveChild(image);
                            }
                            else
                            {
                                foreach(var attribute in image.Attributes)
                                {
                                    if (attribute.NodeName == "src")
                                        attribute.NodeValue = logo;
                                }
                            }
                        }
                    }
                    catch { }

                    var toast = new ToastNotification(toastXml);
                    ToastNotificationManager.CreateToastNotifier().Show(toast);
                }

                // tile
                if (!string.IsNullOrEmpty(message))
                {
                    if (string.IsNullOrEmpty(title))
                        title = string.Empty;

                    var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
                        <tile>
                          <visual>
                            <binding template=""TileMedium"" branding=""name"">
                                  <image placement=""peek"" src=""Assets/Square150x150Logo.png""/>
                                  <text tf=""title"" hint-style=""caption""></text>
                                  <text tf=""message"" hint-style=""captionsubtle"" hint-wrap=""true""></text>
                            </binding>

                            <binding template=""TileWide"">
                                  <image placement=""peek"" src=""Assets/Wide310x150Logo.png""/>
                                  <text tf=""title"" hint-style=""body""></text>
                                  <text tf=""message"" hint-style=""captionSubtle"" hint-wrap=""true""></text>
                            </binding>

                            <binding template=""TileLarge"">
                                  <image placement=""peek"" src=""Assets/Square310x310Logo.png""/>
                                  <text tf=""title"" hint-style=""body""></text>
                                  <text tf=""message"" hint-style=""captionSubtle"" hint-wrap=""true""></text>
                            </binding>
                          </visual>
                        </tile>";

                    if (!string.IsNullOrEmpty(logo) && !noTickerLogo)
                    {
                        xml = string.Format(@"<?xml version=""1.0"" encoding=""UTF-8""?>
                        <tile>
                          <visual>
                            <binding template=""TileMedium"" branding=""name"">
                                  <image placement=""peek"" src=""Assets/Square150x150Logo.png""/>

                                  <text tf=""title"" hint-style=""caption""></text>
                                  <text tf=""message"" hint-style=""captionsubtle"" hint-wrap=""true""></text>
                            </binding>

                            <binding template=""TileWide"">
                                  <image placement=""peek"" src=""Assets/Wide310x150Logo.png""/>

                                  <group>
                                    <subgroup hint-weight=""33"">
                                      <image src=""{0}""/>
                                    </subgroup>

                                    <subgroup hint-textStacking=""center"">
                                      <text tf=""title"" hint-style=""body""></text>
                                      <text tf=""message"" hint-style=""captionSubtle"" hint-wrap=""true""></text>
                                    </subgroup>
                                  </group>
                            </binding>

                            <binding template=""TileLarge"">
                                  <image placement=""peek"" src=""Assets/Square310x310Logo.png""/>

                                  <group>
                                    <subgroup hint-weight=""1""/>
                                    <subgroup hint-weight=""2"">
                                      <image src=""{0}""/>
                                    </subgroup>
                                    <subgroup hint-weight=""1""/>
                                  </group>

                                  <text tf=""title"" hint-style=""body"" hint-align=""center""></text>
                                  <text tf=""message"" hint-style=""captionSubtle"" hint-align=""center"" hint-wrap=""true""></text>
                            </binding>
                          </visual>
                        </tile>", logo);
                    }

                    var tileXml = new Windows.Data.Xml.Dom.XmlDocument();
                    tileXml.LoadXml(xml);

                    var tileTexts = tileXml.GetElementsByTagName("text");
                    foreach (var tileText in tileTexts)
                    {
                        foreach (var attribute in tileText.Attributes)
                        {
                            if (attribute.NodeName == "tf" && attribute.NodeValue.ToString() == "title")
                                tileText.InnerText = title;
                            else if (attribute.NodeName == "tf" && attribute.NodeValue.ToString() == "message")
                                tileText.InnerText = message;
                        }
                    }

                    var tile = new TileNotification(tileXml) { ExpirationTime = DateTime.Now.AddHours(24) };

                    var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                    updater.EnableNotificationQueue(true);
                    updater.Update(tile);
                }

                int badgeCount = 0;
                try
                {
                    object tmp = null;
                    settings.TryGetValue("badgeCount", out tmp);

                    if (tmp != null)
                        badgeCount = (int)tmp;
                    badgeCount++;
                    settings["badgeCount"] = badgeCount;
                }
                catch { }

                var badgeXml = new Windows.Data.Xml.Dom.XmlDocument();
                badgeXml.LoadXml(string.Format(@"<?xml version=""1.0"" encoding=""UTF-8""?><badge value=""{0}""></badge>", badgeCount));
                BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(new BadgeNotification(badgeXml));
            }
        }
    }
}
