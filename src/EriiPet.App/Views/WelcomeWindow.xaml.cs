using System.Windows;

namespace EriiPet.App.Views;

public partial class WelcomeWindow : Window
{
    public bool DontShowAgainChecked => DontShowAgain.IsChecked == true;

    public WelcomeWindow()
    {
        InitializeComponent();
    }

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
