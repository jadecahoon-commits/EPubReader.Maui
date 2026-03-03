using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.Maui.Authentication;

namespace EPubReader.Maui;

/// <summary>
/// Handles Google OAuth2 sign-in via the device browser (WebAuthenticator),
/// token storage via SecureStorage, and Drive file operations.
/// Works on Android; desktop platforms use a stub.
/// </summary>
public class GoogleAuthService
{
    // ── Replace with your actual credentials from Google Cloud Console ────────
    // Android client IDs do NOT use a client secret — leave ClientSecret empty.
    // For the redirect URI, use:  com.companyname.epubreader.maui:/oauth2redirect
    private const string ClientId = "1066066556651-8j97fut2db7ct0rl2lrh3bkbma5a26l7.apps.googleusercontent.com";
    private const string ClientSecret = ""; // Not used for Android installed-app flow
    private const string RedirectUri = "com.companyname.epubreader.maui:/oauth2redirect";

    private const string DriveFileName = "library-data.json";

    // SecureStorage keys
    private const string KeyAccessToken = "gdrive_access_token";
    private const string KeyRefreshToken = "gdrive_refresh_token";
    private const string KeyUserEmail = "gdrive_user_email";

    private const string KeyLibraryFolderId = "gdrive_library_folder_id";
    private const string KeyLibraryFolderName = "gdrive_library_folder_name";

    private string? _libraryFolderId;
    private string? _libraryFolderName;

    public string? LibraryFolderId => _libraryFolderId;
    public string? LibraryFolderName => _libraryFolderName;

    public async Task SetLibraryFolderAsync(string folderId, string folderName)
    {
        _libraryFolderId = folderId;
        _libraryFolderName = folderName;
        await SecureStorage.Default.SetAsync(KeyLibraryFolderId, folderId);
        await SecureStorage.Default.SetAsync(KeyLibraryFolderName, folderName);
    }

    public void ClearLibraryFolder()
    {
        _libraryFolderId = null;
        _libraryFolderName = null;
        SecureStorage.Default.Remove(KeyLibraryFolderId);
        SecureStorage.Default.Remove(KeyLibraryFolderName);
    }

    // ── Singleton ─────────────────────────────────────────────────────────────

    public static readonly GoogleAuthService Instance = new();
    private GoogleAuthService() { }

    // ── State ─────────────────────────────────────────────────────────────────

    private string? _accessToken;
    private string? _refreshToken;
    private string? _userEmail;

    public bool IsSignedIn => !string.IsNullOrEmpty(_accessToken);
    public string? UserEmail => _userEmail;

    // ── Initialise (call on app start to restore saved session) ───────────────

    public async Task InitAsync()
    {
        try
        {
            _accessToken = await SecureStorage.Default.GetAsync(KeyAccessToken);
            _refreshToken = await SecureStorage.Default.GetAsync(KeyRefreshToken);
            _userEmail = await SecureStorage.Default.GetAsync(KeyUserEmail);
            _libraryFolderId = await SecureStorage.Default.GetAsync(KeyLibraryFolderId);
            _libraryFolderName = await SecureStorage.Default.GetAsync(KeyLibraryFolderName);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GoogleAuthService.InitAsync: {ex.Message}");
        }
    }

    // ── Sign in ───────────────────────────────────────────────────────────────

    public async Task<bool> SignInAsync()
    {
        try
        {
            var state = Guid.NewGuid().ToString("N");
            var scopes = Uri.EscapeDataString(
                "https://www.googleapis.com/auth/drive.file " +
                "https://www.googleapis.com/auth/userinfo.email");

            var authUrl =
                $"https://accounts.google.com/o/oauth2/v2/auth" +
                $"?client_id={Uri.EscapeDataString(ClientId)}" +
                $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                $"&response_type=code" +
                $"&scope={scopes}" +
                $"&state={state}" +
                $"&access_type=offline" +
                $"&prompt=consent";

            WebAuthenticatorResult? result = null;
            string webAuthError = "none";

            try
            {
                result = await WebAuthenticator.Default.AuthenticateAsync(
                    new WebAuthenticatorOptions
                    {
                        Url = new Uri(authUrl),
                        CallbackUrl = new Uri(RedirectUri),
                        PrefersEphemeralWebBrowserSession = true
                    });
            }
            catch (TaskCanceledException)
            {
                webAuthError = "TaskCancelled";
                return false;
            }
            catch (Exception ex)
            {
                webAuthError = $"{ex.GetType().Name}: {ex.Message}";
            }

            // Show diagnostic alert
            await Application.Current!.MainPage!.DisplayAlert("GDrive Diagnostic",
                $"WebAuth error: {webAuthError}\n" +
                $"Result null: {result == null}\n" +
                $"Properties: {(result == null ? "n/a" : string.Join(", ", result.Properties.Select(kv => $"{kv.Key}={kv.Value}")))}",
                "OK");

            if (result == null) return false;

            result.Properties.TryGetValue("code", out var code);
            if (string.IsNullOrEmpty(code)) return false;

            var tokenResponse = await ExchangeCodeForTokensAsync(code);
            if (tokenResponse == null) return false;

            _accessToken = tokenResponse.AccessToken;
            _refreshToken = tokenResponse.RefreshToken;
            _userEmail = await FetchUserEmailAsync(_accessToken!);

            await SecureStorage.Default.SetAsync(KeyAccessToken, _accessToken ?? "");
            await SecureStorage.Default.SetAsync(KeyRefreshToken, _refreshToken ?? "");
            await SecureStorage.Default.SetAsync(KeyUserEmail, _userEmail ?? "");

            return true;
        }
        catch (Exception ex)
        {
            await Application.Current!.MainPage!.DisplayAlert("GDrive Outer Exception",
                $"{ex.GetType().Name}: {ex.Message}", "OK");
            return false;
        }
    }

    // ── Sign out ──────────────────────────────────────────────────────────────

    public void SignOut()
    {
        _accessToken = null;
        _refreshToken = null;
        _userEmail = null;

        SecureStorage.Default.Remove(KeyAccessToken);
        SecureStorage.Default.Remove(KeyRefreshToken);
        SecureStorage.Default.Remove(KeyUserEmail);
    }

    // ── Upload library-data.json to Drive ─────────────────────────────────────

    /// <summary>
    /// Uploads the given local file to Google Drive as library-data.json.
    /// Creates the file on first run; updates it on subsequent runs.
    /// </summary>
    public async Task<bool> UploadLibraryDataAsync(string localFilePath)
    {
        try
        {
            if (!await EnsureValidTokenAsync()) return false;
            if (!File.Exists(localFilePath))
            {
                Debug.WriteLine("UploadLibraryData: local file not found");
                return false;
            }

            var service = BuildDriveService();
            var existingFileId = await FindDriveFileIdAsync(service);

            await using var stream = File.OpenRead(localFilePath);
            var mimeType = "application/json";

            if (existingFileId == null)
            {
                // Create
                var fileMetadata = new Google.Apis.Drive.v3.Data.File { Name = DriveFileName };
                var createRequest = service.Files.Create(fileMetadata, stream, mimeType);
                createRequest.Fields = "id";
                var progress = await createRequest.UploadAsync();
                return progress.Status == Google.Apis.Upload.UploadStatus.Completed;
            }
            else
            {
                // Update
                var updateRequest = service.Files.Update(
                    new Google.Apis.Drive.v3.Data.File(), existingFileId, stream, mimeType);
                var progress = await updateRequest.UploadAsync();
                return progress.Status == Google.Apis.Upload.UploadStatus.Completed;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UploadLibraryData: {ex.Message}");
            return false;
        }
    }

    // ── Download library-data.json from Drive ─────────────────────────────────

    /// <summary>
    /// Downloads library-data.json from Drive into the given local path.
    /// Returns false if the file doesn't exist on Drive yet.
    /// </summary>
    public async Task<bool> DownloadLibraryDataAsync(string localFilePath)
    {
        try
        {
            if (!await EnsureValidTokenAsync()) return false;

            var service = BuildDriveService();
            var fileId = await FindDriveFileIdAsync(service);
            if (fileId == null) return false;

            var dir = Path.GetDirectoryName(localFilePath);
            if (dir != null) Directory.CreateDirectory(dir);

            await using var output = File.Create(localFilePath);
            var getRequest = service.Files.Get(fileId);
            await getRequest.DownloadAsync(output);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DownloadLibraryData: {ex.Message}");
            return false;
        }
    }

    // ── Drive folder browsing ─────────────────────────────────────────────────

    public record DriveFolderItem(string Id, string Name);

    /// <summary>Lists subfolders of a given parent folder ID ("root" for My Drive root).</summary>
    public async Task<List<DriveFolderItem>> ListFoldersAsync(string parentId = "root")
    {
        if (!await EnsureValidTokenAsync()) return [];

        var service = BuildDriveService();
        var request = service.Files.List();
        request.Q = $"'{parentId}' in parents and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
        request.Fields = "files(id, name)";
        request.OrderBy = "name";
        request.PageSize = 100;

        var result = await request.ExecuteAsync();
        return result.Files
            .Select(f => new DriveFolderItem(f.Id, f.Name))
            .ToList();
    }

    /// <summary>Gets the name of a folder by ID (used to display breadcrumb).</summary>
    public async Task<string?> GetFolderNameAsync(string folderId)
    {
        if (!await EnsureValidTokenAsync()) return null;

        var service = BuildDriveService();
        var request = service.Files.Get(folderId);
        request.Fields = "name";
        var file = await request.ExecuteAsync();
        return file.Name;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private DriveService BuildDriveService()
    {
        var credential = GoogleCredential
            .FromAccessToken(_accessToken)
            .CreateScoped(DriveService.Scope.DriveFile);

        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "EPubReader"
        });
    }

    private static async Task<string?> FindDriveFileIdAsync(DriveService service)
    {
        var listRequest = service.Files.List();
        listRequest.Q = $"name = '{DriveFileName}' and trashed = false";
        listRequest.Fields = "files(id, name)";
        listRequest.Spaces = "drive";
        listRequest.PageSize = 1;

        var result = await listRequest.ExecuteAsync();
        return result.Files?.FirstOrDefault()?.Id;
    }

    private async Task<bool> EnsureValidTokenAsync()
    {
        if (string.IsNullOrEmpty(_accessToken)) return false;

        // Try a lightweight token-info call; if it fails, attempt refresh
        using var http = new HttpClient();
        var check = await http.GetAsync(
            $"https://oauth2.googleapis.com/tokeninfo?access_token={_accessToken}");

        if (check.IsSuccessStatusCode) return true;

        // Token expired — refresh
        if (!string.IsNullOrEmpty(_refreshToken))
            return await RefreshAccessTokenAsync();

        return false;
    }

    private async Task<bool> RefreshAccessTokenAsync()
    {
        try
        {
            using var http = new HttpClient();
            var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["refresh_token"] = _refreshToken!,
                ["grant_type"] = "refresh_token"
            });

            var response = await http.PostAsync("https://oauth2.googleapis.com/token", body);
            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("access_token", out var tokenEl)) return false;
            _accessToken = tokenEl.GetString();

            await SecureStorage.Default.SetAsync(KeyAccessToken, _accessToken ?? "");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RefreshAccessToken: {ex.Message}");
            return false;
        }
    }

    private record TokenResponse(string? AccessToken, string? RefreshToken);

    private static async Task<TokenResponse?> ExchangeCodeForTokensAsync(string code)
    {
        try
        {
            using var http = new HttpClient();
            var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = ClientId,
                ["redirect_uri"] = RedirectUri,
                ["grant_type"] = "authorization_code"
            });

            var response = await http.PostAsync("https://oauth2.googleapis.com/token", body);
            var responseText = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"[GDrive] Token exchange status: {response.StatusCode}");
            Debug.WriteLine($"[GDrive] Token exchange response: {responseText}");

            if (!response.IsSuccessStatusCode) return null;


            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            doc.RootElement.TryGetProperty("access_token", out var at);
            doc.RootElement.TryGetProperty("refresh_token", out var rt);

            return new TokenResponse(at.GetString(), rt.GetString());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ExchangeCodeForTokens: {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> FetchUserEmailAsync(string accessToken)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
            var json = await http.GetStringAsync("https://www.googleapis.com/oauth2/v3/userinfo");
            var doc = JsonDocument.Parse(json);
            doc.RootElement.TryGetProperty("email", out var email);
            return email.GetString();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FetchUserEmail: {ex.Message}");
            return null;
        }
    }
}