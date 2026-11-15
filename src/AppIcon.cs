using System;
using System.Drawing;
using System.IO;

namespace RussiaFlag
{
    internal static class AppIcon
    {
        internal static Icon Load()
        {
            using (Stream stream = typeof(AppIcon).Assembly.GetManifestResourceStream("RussiaFlag.Tray.ico"))
            {
                if (stream == null) throw new InvalidOperationException("Tray flag icon resource is missing");
                using (Icon icon = new Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize)) return (Icon)icon.Clone();
            }
        }
    }
}
