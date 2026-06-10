using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Windows;
using Serilog;

namespace BmwCarDataClient
{
    public partial class App : Application
    {
        public static IServiceProvider ServiceProvider { get; private set; }

        public App()
        {
            InitializeComponent();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File("Logs/BmwApplication.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (!File.Exists(settingsPath))
            {
                var setupWindow = new SettingsSetupWindow();
                setupWindow.ShowDialog();

                if (!setupWindow.IsConfigurationSaved)
                {
                    // User cancelled the setup, exit the application
                    Current.Shutdown();
                    return;
                }
            }

            ConfigureServices();

            // Show main window from DI container
            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        private void ConfigureServices()
        {
            var services = new ServiceCollection();

            // Load configuration from appsettings.json
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            services.AddSingleton<IConfiguration>(configuration);

            // Register HttpClient with proper configuration for BmwRestService
            services.AddHttpClient<BmwRestService>((provider, client) =>
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                    client.DefaultRequestHeaders.Add("User-Agent", "BmwCarDataClient/1.0");
                })
                .AddTypedClient<BmwRestService>((client, provider) => 
                {
                    var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "rest");
                    Action<string> logger = (msg) => 
                    {
                        Log.Information(msg);
                    };
                    return new BmwRestService(client, outputDir, logger);
                });

            // Register other services as singletons
            services.AddSingleton<MainWindow>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<BmwOAuthService>();
            services.AddSingleton<BmwMqttService>();
            services.AddSingleton<ProtectedTokenStore>();

            ServiceProvider = services.BuildServiceProvider();
        }

    }
}
