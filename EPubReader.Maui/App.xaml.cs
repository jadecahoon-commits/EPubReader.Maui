namespace EPubReader.Maui
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            LibraryData.Load();
            UserAppTheme = LibraryData.Theme == "Dark" ? AppTheme.Dark : AppTheme.Light;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }
    }
}