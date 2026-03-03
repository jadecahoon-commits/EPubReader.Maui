namespace EPubReader.Maui
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            LibraryData.Load();
            UserAppTheme = LibraryData.Theme == "Dark" ? AppTheme.Dark : AppTheme.Light;

            // Restore Google Drive token from SecureStorage in the background
            #if ANDROID
                        _ = GoogleAuthService.Instance.InitAsync();
            #endif       
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }
    }
}