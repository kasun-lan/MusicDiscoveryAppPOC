using System.Configuration;
using System.Data;
using System.Runtime.InteropServices;
using System.Windows;

namespace MusicDiscoveryAppPOC
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        protected override void OnStartup(StartupEventArgs e)
        {
            AllocConsole();
            Console.WriteLine("Console attached!");
            base.OnStartup(e);
        }
    }

}
