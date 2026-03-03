using Android.Content;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

namespace EPubReader.Maui;

public class GoogleAuthService
{
    private const string ClientId = "YOUR_CLIENT_ID.apps.googleusercontent.com";
    private UserCredential? _credential;

    public async Task<DriveService?> SignInAsync()
    {
        try
        {
            _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                new ClientSecrets { ClientId = ClientId },
                new[] { DriveService.Scope.DriveReadonly },
                "user",
                CancellationToken.None
            );

            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = _credential,
                ApplicationName = "EPubReader"
            });
        }
        catch (Exception ex)
        {
         //   Debug.WriteLine($"Google sign-in failed: {ex.Message}");
            return null;
        }
    }

    public bool IsSignedIn => _credential != null;
}