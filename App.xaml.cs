using System.Windows;
using VLSMCalculator.Views;
using System; // Add System namespace

namespace VLSMCalculator
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // Handle unhandled exceptions to help with debugging
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        }        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // Output to console for debugging
            Console.WriteLine("=== UNHANDLED EXCEPTION ===");
            Console.WriteLine($"Message: {e.Exception.Message}");
            Console.WriteLine($"Stack Trace:\n{e.Exception.StackTrace}");
            Console.WriteLine("========================");
            
            MessageBox.Show($"An unhandled exception occurred: {e.Exception.Message}\n\nStack Trace:\n{e.Exception.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true; // This prevents the application from crashing
        }        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                Console.WriteLine("App starting up...");
                base.OnStartup(e);
                
                Console.WriteLine("Creating main window...");
                // Create and show the main window
                var mainWindow = new MainWindow();
                mainWindow.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
                mainWindow.WindowState = System.Windows.WindowState.Normal;
                Console.WriteLine("Showing main window...");
                mainWindow.Show();
                mainWindow.Activate();
                mainWindow.Topmost = true;
                mainWindow.Topmost = false; // Reset topmost after bringing to front
                Console.WriteLine("Main window shown successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("=== STARTUP EXCEPTION ===");
                Console.WriteLine($"Message: {ex.Message}");
                Console.WriteLine($"Stack Trace:\n{ex.StackTrace}");
                Console.WriteLine("========================");
                
                MessageBox.Show($"Startup error: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }
    }
}
