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
                "https://www.googleapis.com/auth/drive.readonly " +
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
            .CreateScoped(DriveService.Scope.DriveFile, DriveService.Scope.DriveReadonly);

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


    // ── Drive library scanning ────────────────────────────────────────────────────

    /// <summary>
    /// Prefix stored in LibraryData.LibraryPath to indicate a Drive-backed library.
    /// Format: gdrive://&lt;rootFolderId&gt;
    /// </summary>
    public const string DriveLibraryPrefix = "gdrive://";

    private static readonly string[] _driveBookExtensions =
        [".epub", ".mobi", ".azw", ".azw3", ".pdf", ".doc", ".docx", ".txt"];

    private static readonly string[] _driveImageExtensions =
        [".jpg", ".jpeg", ".png"];

    /// <summary>
    /// Walks the Drive folder tree (author → book → files) and returns a BookItem
    /// list that mirrors what LibraryScanner produces for local folders.
    /// FilePath is set to "gdrive://&lt;fileId&gt;" so the rest of the app can
    /// identify it as a Drive file without extra lookups.
    /// </summary>
    public async Task<List<BookItem>> ScanLibraryFolderAsync(string rootFolderId)
    {
        var books = new List<BookItem>();

        if (!await EnsureValidTokenAsync()) return books;

        var service = BuildDriveService();

        // Depth 1 — author folders
        var authorFolders = await ListDriveChildren(service, rootFolderId, foldersOnly: true);

        foreach (var authorFolder in authorFolders)
        {
            var author = authorFolder.Name;

            // Depth 2 — book folders
            var bookFolders = await ListDriveChildren(service, authorFolder.Id, foldersOnly: true);

            foreach (var bookFolder in bookFolders)
            {
                var folderTitle = bookFolder.Name;

                // All files inside the book folder
                var files = await ListDriveChildren(service, bookFolder.Id, foldersOnly: false);

                // Cover image
                var coverFile = files.FirstOrDefault(f =>
                    !f.IsFolder &&
                    _driveImageExtensions.Contains(
                        Path.GetExtension(f.Name).ToLowerInvariant()));
                string? coverUrl = coverFile != null ? BuildDriveDownloadUrl(coverFile.Id) : null;

                // OPF metadata
                string? title = folderTitle;
                string? description = null;
                float seriesIndex = 0f;
                bool isFinished = false;

                var opfFile = files.FirstOrDefault(f =>
                    !f.IsFolder &&
                    f.Name.EndsWith(".opf", StringComparison.OrdinalIgnoreCase));

                if (opfFile != null)
                {
                    var opfContent = await DownloadDriveFileTextAsync(service, opfFile.Id);
                    if (opfContent != null)
                    {
                        var meta = LibraryScanner.ParseOpfMetadataPublic(opfContent);
                        title = meta.Title ?? folderTitle;
                        description = meta.Description;
                        seriesIndex = meta.SeriesIndex;
                        isFinished = meta.IsFinished;
                    }
                }

                // One BookItem per supported book file
                foreach (var file in files)
                {
                    if (file.IsFolder) continue;
                    var ext = Path.GetExtension(file.Name).ToLowerInvariant();
                    if (!_driveBookExtensions.Contains(ext)) continue;

                         var filePath = $"{DriveLibraryPrefix}{file.Id}";
                         var calibreKey = LibraryData.BuildCalibreKey(author, folderTitle, file.Name);
                         books.Add(new BookItem
                         {
                             Title = title ?? folderTitle,
                             Author = author,
                             FilePath = filePath,
                             FileType = ext.TrimStart('.'),
                             CoverImagePath = coverUrl,
                             Description = description,
                             SeriesIndex = seriesIndex,
                             IsFinished = isFinished,
                             CalibreKey = calibreKey,
                             Fandom = LibraryData.GetFandom(calibreKey),
                             Category = LibraryData.GetCategory(calibreKey)
                         });
                }
            }
        }

        return books;
    }

    /// <summary>
    /// Downloads a Drive file (identified by its Drive file ID) into a MemoryStream
    /// so it can be streamed to the epub reader.
    /// </summary>
    public async Task<Stream?> OpenDriveFileStreamAsync(string driveFileId)
    {
        if (!await EnsureValidTokenAsync()) return null;
        try
        {
            var service = BuildDriveService();
            var request = service.Files.Get(driveFileId);
            var ms = new MemoryStream();
            await request.DownloadAsync(ms);
            ms.Position = 0;
            return ms;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenDriveFileStream {driveFileId}: {ex.Message}");
            return null;
        }
    }

    // ── Internal Drive helpers ────────────────────────────────────────────────────

    private record DriveChildItem(string Id, string Name, bool IsFolder);

    private static async Task<List<DriveChildItem>> ListDriveChildren(
        DriveService service, string parentId, bool foldersOnly)
    {
        var request = service.Files.List();
        request.Q = foldersOnly
            ? $"'{parentId}' in parents and mimeType = 'application/vnd.google-apps.folder' and trashed = false"
            : $"'{parentId}' in parents and trashed = false";
        request.Fields = "files(id, name, mimeType)";
        request.OrderBy = "name";
        request.PageSize = 1000;

        var result = await request.ExecuteAsync();
        return result.Files
            .Select(f => new DriveChildItem(
                f.Id,
                f.Name,
                f.MimeType == "application/vnd.google-apps.folder"))
            .ToList();
    }

    private static string BuildDriveDownloadUrl(string fileId) =>
        $"https://www.googleapis.com/drive/v3/files/{fileId}?alt=media";

    private static async Task<string?> DownloadDriveFileTextAsync(DriveService service, string fileId)
    {
        try
        {
            var request = service.Files.Get(fileId);
            using var ms = new MemoryStream();
            await request.DownloadAsync(ms);
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DownloadDriveFileText {fileId}: {ex.Message}");
            return null;
        }
    }


    // ── Add these methods to GoogleAuthService ────────────────────────────────────
    // Place them in the "Drive folder browsing" region, after ListFoldersAsync.

    // ── Drive library manifest scanning ──────────────────────────────────────

    private static readonly string[] BookExtensions =
        [".epub", ".mobi", ".azw", ".azw3", ".pdf", ".doc", ".docx", ".txt"];

    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png"];

    /// <summary>
    /// Walks the Calibre library folder on Drive (2 levels deep: author → book folder)
    /// and builds a DriveLibraryManifest without downloading any book content.
    /// Reports progress via the callback (message, 0–100).
    /// </summary>
    public async Task<DriveLibraryManifest?> ScanLibraryFolderAsync(
        IProgress<(string Message, int Percent)>? progress = null)
    {
        if (!await EnsureValidTokenAsync()) return null;
        if (string.IsNullOrEmpty(_libraryFolderId)) return null;

        var service = BuildDriveService();
        var manifest = new DriveLibraryManifest
        {
            RootFolderId = _libraryFolderId!,
            LastSynced = DateTime.UtcNow
        };

        progress?.Report(("Listing author folders…", 0));

        // ── Level 1: author folders ───────────────────────────────────────────
        var authorFolders = await ListAllChildFoldersAsync(service, _libraryFolderId!);
        int authorCount = authorFolders.Count;
        int authorsDone = 0;

        foreach (var authorFolder in authorFolders)
        {
            var authorEntry = new DriveAuthorEntry { Name = authorFolder.Name };

            int pct = authorCount == 0 ? 50 : 5 + (authorsDone * 90 / authorCount);
            progress?.Report(($"Scanning {authorFolder.Name}…", pct));

            // ── Level 2: book folders ─────────────────────────────────────────
            var bookFolders = await ListAllChildFoldersAsync(service, authorFolder.Id);

            foreach (var bookFolder in bookFolders)
            {
                var bookEntry = await ScanBookFolderAsync(service, bookFolder.Id, bookFolder.Name);
                if (bookEntry.Files.Count > 0)
                    authorEntry.Books.Add(bookEntry);
            }

            if (authorEntry.Books.Count > 0)
                manifest.Authors.Add(authorEntry);

            authorsDone++;
        }

        progress?.Report(("Scan complete", 100));
        return manifest;
    }

    /// <summary>
    /// Scans a single book folder, reading file metadata (no content downloads).
    /// Parses OPF metadata inline by downloading just the small .opf text file.
    /// </summary>
    private async Task<DriveBookEntry> ScanBookFolderAsync(
        DriveService service, string folderId, string folderName)
    {
        var entry = new DriveBookEntry { Title = folderName, FolderName = folderName };

        var files = await ListAllChildFilesAsync(service, folderId);

        // Identify cover, opf, and book files
        foreach (var file in files)
        {
            var ext = Path.GetExtension(file.Name).ToLowerInvariant();

            if (ImageExtensions.Contains(ext) && entry.CoverDriveFileId == null)
            {
                entry.CoverDriveFileId = file.Id;
            }
            else if (ext == ".opf" && entry.OpfDriveFileId == null)
            {
                entry.OpfDriveFileId = file.Id;
            }
            else if (BookExtensions.Contains(ext))
            {
                entry.Files.Add(new DriveBookFile
                {
                    DriveFileId = file.Id,
                    FileName = file.Name,
                    Extension = ext
                });
            }
        }

        // Download and parse OPF for rich metadata (it's a tiny text file, ~2-4 KB)
        if (entry.OpfDriveFileId != null)
        {
            try
            {
                var opfText = await DownloadFileAsTextAsync(service, entry.OpfDriveFileId);
                if (opfText != null)
                {
                    var meta = ParseOpfMetadata(opfText);
                    entry.Title = meta.Title ?? folderName;
                    entry.Description = meta.Description;
                    entry.SeriesIndex = meta.SeriesIndex;
                    entry.IsFinished = meta.IsFinished;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OPF parse error for {folderName}: {ex.Message}");
            }
        }

        return entry;
    }

    /// <summary>
    /// Downloads a Drive file by ID into a local file path.
    /// Used at read-time when the user opens a book from the Drive library.
    /// </summary>
    public async Task<bool> DownloadFileByIdAsync(string driveFileId, string localPath)
    {
        try
        {
            if (!await EnsureValidTokenAsync()) return false;

            var service = BuildDriveService();
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

            await using var output = File.Create(localPath);
            var getRequest = service.Files.Get(driveFileId);
            var result = await getRequest.DownloadAsync(output);
            return result.Status == Google.Apis.Download.DownloadStatus.Completed;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DownloadFileById {driveFileId}: {ex.Message}");
            return false;
        }
    }

    // ── Internal Drive listing helpers ────────────────────────────────────────

    private static async Task<List<Google.Apis.Drive.v3.Data.File>> ListAllChildFoldersAsync(
        DriveService service, string parentId)
    {
        var request = service.Files.List();
        request.Q = $"'{parentId}' in parents " +
                    $"and mimeType = 'application/vnd.google-apps.folder' " +
                    $"and trashed = false";
        request.Fields = "files(id, name)";
        request.OrderBy = "name";
        request.PageSize = 1000;

        var result = await request.ExecuteAsync();
        return result.Files?.ToList() ?? new();
    }

    private static async Task<List<Google.Apis.Drive.v3.Data.File>> ListAllChildFilesAsync(
        DriveService service, string parentId)
    {
        var request = service.Files.List();
        request.Q = $"'{parentId}' in parents " +
                    $"and mimeType != 'application/vnd.google-apps.folder' " +
                    $"and trashed = false";
        request.Fields = "files(id, name, mimeType)";
        request.PageSize = 100;

        var result = await request.ExecuteAsync();
        return result.Files?.ToList() ?? new();
    }

    private static async Task<string?> DownloadFileAsTextAsync(DriveService service, string fileId)
    {
        await using var stream = new MemoryStream();
        var getRequest = service.Files.Get(fileId);
        var result = await getRequest.DownloadAsync(stream);
        if (result.Status != Google.Apis.Download.DownloadStatus.Completed) return null;
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    // ── OPF parsing (duplicated from AndroidLibraryScanner to keep service self-contained) ──

    private static (string? Title, string? Description, float SeriesIndex, bool IsFinished)
        ParseOpfMetadata(string content)
    {
        var title = ExtractOpfField(content, "dc:title");
        var description = ExtractOpfField(content, "dc:description");

        if (description != null)
        {
            description = System.Net.WebUtility.HtmlDecode(description);
            description = System.Text.RegularExpressions.Regex.Replace(description, "<.*?>", "");
            if (string.IsNullOrWhiteSpace(description)) description = null;
        }

        float seriesIndex = 0f;
        var seriesIndexStr = ExtractMetaContent(content, "calibre:series_index");
        if (seriesIndexStr != null)
            float.TryParse(seriesIndexStr,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out seriesIndex);

        bool isFinished = false;
        var finishedMeta = ExtractMetaContent(content, "calibre:user_metadata:#finished");
        if (finishedMeta != null)
        {
            var decoded = System.Net.WebUtility.HtmlDecode(finishedMeta);
            var match = System.Text.RegularExpressions.Regex.Match(
                decoded, @"""#value#""\s*:\s*(true|false|null)");
            if (match.Success)
                isFinished = match.Groups[1].Value == "true";
        }

        return (title, description, seriesIndex, isFinished);
    }

    private static string? ExtractOpfField(string content, string tagName)
    {
        var startTag = $"<{tagName}>";
        var endTag = $"</{tagName}>";
        var start = content.IndexOf(startTag);
        if (start < 0) return null;
        start += startTag.Length;
        var end = content.IndexOf(endTag, start);
        if (end < 0) return null;
        var value = content[start..end].Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ExtractMetaContent(string content, string nameValue)
    {
        var marker = $"name=\"{nameValue}\"";
        var idx = content.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var tagStart = content.LastIndexOf('<', idx);
        if (tagStart < 0) return null;
        var tagEnd = content.IndexOf('>', idx);
        if (tagEnd < 0) return null;
        var tag = content[tagStart..tagEnd];
        var contentMarker = "content=\"";
        var ci = tag.IndexOf(contentMarker, StringComparison.OrdinalIgnoreCase);
        if (ci < 0) return null;
        ci += contentMarker.Length;
        var ce = tag.IndexOf('"', ci);
        if (ce < 0) return null;
        return tag[ci..ce];
    }
}