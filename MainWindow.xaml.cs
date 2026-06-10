using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;
using ModernWpf.Controls;

namespace BmwCarDataClient
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            // Get services from DI container
            var oauthService = App.ServiceProvider.GetRequiredService<BmwOAuthService>();
            var restService = App.ServiceProvider.GetRequiredService<BmwRestService>();
            var mqttService = App.ServiceProvider.GetRequiredService<BmwMqttService>();

            // Set DataContext with ViewModel
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.InitializeServices(oauthService, restService, mqttService);
            }
        }

        private void MainNavigation_Loaded(object sender, RoutedEventArgs e)
        {
            // Seleziona visivamente il primo elemento nel menu (BMW Account & Auth)
            if (MainNavigation.MenuItems.Count > 0)
            {
                MainNavigation.SelectedItem = MainNavigation.MenuItems[0];
            }
            
            // Forza il caricamento della vista Account all'avvio
            NavigateToView("Account");
        }

        private void MainNavigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                NavigateToView("Settings");
            }
            else if (args.InvokedItemContainer != null)
            {
                string tag = args.InvokedItemContainer.Tag?.ToString();
                NavigateToView(tag);
            }
        }

        private void NavigateToView(string viewTag)
        {
            // Frame does not automatically inherit DataContext, so we pass it explicitly
            switch (viewTag)
            {
                case "Dashboard":
                    ContentFrame.Navigate(new Views.DashboardView { DataContext = this.DataContext });
                    break;
                case "VehicleInfo":
                    ContentFrame.Navigate(new Views.VehicleInfoView { DataContext = this.DataContext });
                    break;
                case "Containers":
                    ContentFrame.Navigate(new Views.ContainersView { DataContext = this.DataContext });
                    break;
                case "Account":
                    ContentFrame.Navigate(new Views.AccountView { DataContext = this.DataContext });
                    break;
                case "Settings":
                    ContentFrame.Navigate(new Views.SettingsView { DataContext = new ViewModels.SettingsViewModel() });
                    break;
            }
        }
    }
}
