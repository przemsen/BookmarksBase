using Microsoft.Extensions.Configuration;
using System.IO;
using System.Windows;

namespace BookmarksBase.Search;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public readonly Settings Settings;

    public App()
    {
        var configurationBuilder =  new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.search.json", optional: false)
            ;
        var configuration = configurationBuilder.Build();

        Settings = new Settings();

        configuration.GetRequiredSection(nameof(Settings)).Bind(Settings);

    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {

    }
}
