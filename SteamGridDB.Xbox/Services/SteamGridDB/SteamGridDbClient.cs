using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Windows.Web.Http;
using Windows.Web.Http.Headers;

using SteamGridDB.Xbox.Services.SteamGridDB.Models;

namespace SteamGridDB.Xbox.Services.SteamGridDB
{
    /// <summary>
    /// Client for interacting with the SteamGridDB API.
    /// Documentation: https://www.steamgriddb.com/api/v2
    /// </summary>
    public class SteamGridDbClient : IDisposable
    {
        private readonly HttpClient httpClient;
        private readonly string baseUrl = "https://www.steamgriddb.com/api/v2";
        private readonly TimeSpan timeout = TimeSpan.FromSeconds(30);
        private bool disposed = false;

        /// <summary>
        /// Initialises a new SteamGridDB client with API key.
        /// </summary>
        /// <param name="apiKey">SteamGridDB API key.</param>
        /// <param name="timeoutSeconds">Request timeout in seconds (default is 30).</param>
        public SteamGridDbClient(string apiKey, int timeoutSeconds = 30)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("API key is required", nameof(apiKey));
            }

            if (timeoutSeconds <= 0)
            {
                throw new ArgumentException("Timeout must be greater than 0", nameof(timeoutSeconds));
            }

            timeout = TimeSpan.FromSeconds(timeoutSeconds);

            httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            httpClient.DefaultRequestHeaders.Accept.Add(new HttpMediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <summary>
        /// Searches for a game by name.
        /// </summary>
        /// <param name="term">Search term.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of matching games.</returns>
        public async Task<List<SteamGridDbGame>> SearchGameByNameAsync(string term, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                throw new ArgumentException("Search term cannot be empty", nameof(term));
            }

            var url = $"{baseUrl}/search/autocomplete/{Uri.EscapeDataString(term)}";
            var response = await GetAsync<SteamGridDbResponse<List<SteamGridDbGame>>>(url, cancellationToken);

            if (response != null && response.Success && response.Data != null)
            {
                return response.Data;
            }

            return new List<SteamGridDbGame>();
        }

        /// <summary>
        /// Gets game by platform-specific ID (e.g., Steam ID, GOG ID).
        /// </summary>
        /// <param name="platform">Platform type (steam, gog, epic, etc).</param>
        /// <param name="platformId">Platform-specific game ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Game information or null.</returns>
        public async Task<SteamGridDbGame> GetGameByPlatformIdAsync(string platform, string platformId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(platform))
            {
                throw new ArgumentException("Platform cannot be empty", nameof(platform));
            }

            if (string.IsNullOrWhiteSpace(platformId))
            {
                throw new ArgumentException("Platform ID cannot be empty", nameof(platformId));
            }

            var url = $"{baseUrl}/games/{platform}/{Uri.EscapeDataString(platformId)}";
            var response = await GetAsync<SteamGridDbResponse<SteamGridDbGame>>(url, cancellationToken);

            if (response != null && response.Success)
            {
                return response.Data;
            }

            return null;
        }

        /// <summary>
        /// Gets grids (box art) for a game by platform ID.
        /// </summary>
        /// <param name="platform">Platform type (steam, gog, epic, etc).</param>
        /// <param name="platformId">Platform-specific game ID.</param>
        /// <param name="dimensions">Preferred dimensions (e.g., new[] { "600x900", "920x430" }). Use null for all sizes.</param>
        /// <param name="styles">Styles to filter by (e.g., new[] { "alternate", "white_logo" }). Use null for all styles.</param>
        /// <param name="squareOnly">If true, only returns square images (width == height).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of available grids.</returns>
        public async Task<List<SteamGridDbGrid>> GetGridsByPlatformIdAsync(string platform, string platformId, string[] dimensions = null, string[] styles = null, bool squareOnly = false, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(platform))
            {
                throw new ArgumentException("Platform cannot be empty", nameof(platform));
            }

            if (string.IsNullOrWhiteSpace(platformId))
            {
                throw new ArgumentException("Platform ID cannot be empty", nameof(platformId));
            }

            var urlBuilder = new StringBuilder($"{baseUrl}/grids/{platform}/{Uri.EscapeDataString(platformId)}");
            var queryParams = new List<string>();

            if (dimensions != null && dimensions.Length > 0)
            {
                // API expects: ?dimensions[]=600x900&dimensions[]=920x430
                foreach (var dimension in dimensions)
                {
                    queryParams.Add($"dimensions[]={Uri.EscapeDataString(dimension)}");
                }
            }

            if (styles != null && styles.Length > 0)
            {
                // API expects: ?styles[]=alternate&styles[]=white_logo
                foreach (var style in styles)
                {
                    queryParams.Add($"styles[]={Uri.EscapeDataString(style)}");
                }
            }

            if (queryParams.Count > 0)
            {
                urlBuilder.Append("?");
                urlBuilder.Append(string.Join("&", queryParams));
            }

            var response = await GetAsync<SteamGridDbResponse<List<SteamGridDbGrid>>>(urlBuilder.ToString(), cancellationToken);

            if (response != null && response.Success && response.Data != null)
            {
                var grids = response.Data;

                // Filter for square images if requested
                if (squareOnly)
                {
                    grids = FilterSquareImages(grids);
                }

                return grids;
            }

            return new List<SteamGridDbGrid>();
        }

        /// <summary>
        /// Gets square grids (box art) for a game by platform ID - convenience method.
        /// </summary>
        /// <param name="platform">Platform type (steam, gog, epic, etc).</param>
        /// <param name="platformId">Platform-specific game ID.</param>
        /// <param name="styles">Styles to filter by. Use null for all styles.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of square grids only.</returns>
        public async Task<List<SteamGridDbGrid>> GetSquareGridsByPlatformIdAsync(string platform, string platformId, string[] styles = null, CancellationToken cancellationToken = default)
        {
            return await GetGridsByPlatformIdAsync(platform, platformId, null, styles, squareOnly: true, cancellationToken);
        }

        /// <summary>
        /// Gets heroes (wide banners) for a game by platform ID.
        /// </summary>
        /// <param name="platform">Platform type (steam, gog, epic, etc).</param>
        /// <param name="platformId">Platform-specific game ID.</param>
        /// <param name="dimensions">Preferred dimensions (e.g., new[] { "1920x620", "3840x1240" }).</param>
        /// <param name="styles">Styles to filter by (e.g., new[] { "alternate", "blurred" }).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of available heroes.</returns>
        public async Task<List<SteamGridDbGrid>> GetHeroesByPlatformIdAsync(string platform, string platformId, string[] dimensions = null, string[] styles = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(platform))
            {
                throw new ArgumentException("Platform cannot be empty", nameof(platform));
            }

            if (string.IsNullOrWhiteSpace(platformId))
            {
                throw new ArgumentException("Platform ID cannot be empty", nameof(platformId));
            }

            var urlBuilder = new StringBuilder($"{baseUrl}/heroes/{platform}/{Uri.EscapeDataString(platformId)}");
            var queryParams = new List<string>();

            if (dimensions != null && dimensions.Length > 0)
            {
                foreach (var dimension in dimensions)
                {
                    queryParams.Add($"dimensions[]={Uri.EscapeDataString(dimension)}");
                }
            }

            if (styles != null && styles.Length > 0)
            {
                foreach (var style in styles)
                {
                    queryParams.Add($"styles[]={Uri.EscapeDataString(style)}");
                }
            }

            if (queryParams.Count > 0)
            {
                urlBuilder.Append("?");
                urlBuilder.Append(string.Join("&", queryParams));
            }

            var response = await GetAsync<SteamGridDbResponse<List<SteamGridDbGrid>>>(urlBuilder.ToString(), cancellationToken);

            if (response != null && response.Success && response.Data != null)
            {
                return response.Data;
            }

            return new List<SteamGridDbGrid>();
        }

        /// <summary>
        /// Gets logos for a game by platform ID.
        /// </summary>
        /// <param name="platform">Platform type (steam, gog, epic, etc).</param>
        /// <param name="platformId">Platform-specific game ID.</param>
        /// <param name="styles">Styles to filter by (e.g., new[] { "official", "white", "black" }).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of available logos.</returns>
        public async Task<List<SteamGridDbGrid>> GetLogosByPlatformIdAsync(string platform, string platformId, string[] styles = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(platform))
            {
                throw new ArgumentException("Platform cannot be empty", nameof(platform));
            }

            if (string.IsNullOrWhiteSpace(platformId))
            {
                throw new ArgumentException("Platform ID cannot be empty", nameof(platformId));
            }

            var urlBuilder = new StringBuilder($"{baseUrl}/logos/{platform}/{Uri.EscapeDataString(platformId)}");
            var queryParams = new List<string>();

            if (styles != null && styles.Length > 0)
            {
                foreach (var style in styles)
                {
                    queryParams.Add($"styles[]={Uri.EscapeDataString(style)}");
                }
            }

            if (queryParams.Count > 0)
            {
                urlBuilder.Append("?");
                urlBuilder.Append(string.Join("&", queryParams));
            }

            var response = await GetAsync<SteamGridDbResponse<List<SteamGridDbGrid>>>(urlBuilder.ToString(), cancellationToken);

            if (response != null && response.Success && response.Data != null)
            {
                return response.Data;
            }

            return new List<SteamGridDbGrid>();
        }

        /// <summary>
        /// Gets icons for a game by platform ID.
        /// </summary>
        /// <param name="platform">Platform type (steam, gog, epic, etc).</param>
        /// <param name="platformId">Platform-specific game ID.</param>
        /// <param name="dimensions">Preferred dimensions (e.g., new[] { "32", "64", "128" }). Use null for all sizes.</param>
        /// <param name="styles">Styles to filter by (e.g., new[] { "official", "custom" }). Use null for all styles.</param>
        /// <param name="squareOnly">If true, only returns square icons (width == height). Icons are typically square.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of available icons.</returns>
        public async Task<List<SteamGridDbGrid>> GetIconsByPlatformIdAsync(string platform, string platformId, string[] dimensions = null, string[] styles = null, bool squareOnly = false, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(platform))
            {
                throw new ArgumentException("Platform cannot be empty", nameof(platform));
            }

            if (string.IsNullOrWhiteSpace(platformId))
            {
                throw new ArgumentException("Platform ID cannot be empty", nameof(platformId));
            }

            var urlBuilder = new StringBuilder($"{baseUrl}/icons/{platform}/{Uri.EscapeDataString(platformId)}");
            var queryParams = new List<string>();

            if (dimensions != null && dimensions.Length > 0)
            {
                foreach (var dimension in dimensions)
                {
                    queryParams.Add($"dimensions[]={Uri.EscapeDataString(dimension)}");
                }
            }

            if (styles != null && styles.Length > 0)
            {
                foreach (var style in styles)
                {
                    queryParams.Add($"styles[]={Uri.EscapeDataString(style)}");
                }
            }

            if (queryParams.Count > 0)
            {
                urlBuilder.Append("?");
                urlBuilder.Append(string.Join("&", queryParams));
            }

            var response = await GetAsync<SteamGridDbResponse<List<SteamGridDbGrid>>>(urlBuilder.ToString(), cancellationToken);

            if (response != null && response.Success && response.Data != null)
            {
                var icons = response.Data;

                // Filter for square images if requested (most icons are square anyway)
                if (squareOnly)
                {
                    icons = FilterSquareImages(icons);
                }

                return icons;
            }

            return new List<SteamGridDbGrid>();
        }

        /// <summary>
        /// Gets square icons for a game by platform ID - convenience method.
        /// Icons are typically square, but this ensures only 1:1 ratio icons are returned.
        /// </summary>
        /// <param name="platform">Platform type (steam, gog, epic, etc).</param>
        /// <param name="platformId">Platform-specific game ID.</param>
        /// <param name="dimensions">Preferred dimensions (e.g., new[] { "32", "64", "128" }). Use null for all sizes.</param>
        /// <param name="styles">Styles to filter by. Use null for all styles.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of square icons only.</returns>
        public async Task<List<SteamGridDbGrid>> GetSquareIconsByPlatformIdAsync(string platform, string platformId, string[] dimensions = null, string[] styles = null, CancellationToken cancellationToken = default)
        {
            return await GetIconsByPlatformIdAsync(platform, platformId, dimensions, styles, squareOnly: true, cancellationToken);
        }

        /// <summary>
        /// Filters grids to only include square images (width == height).
        /// Note: This parses dimensions from the URL since API doesn't return dimensions directly.
        /// </summary>
        private List<SteamGridDbGrid> FilterSquareImages(List<SteamGridDbGrid> grids)
        {
            var squareGrids = new List<SteamGridDbGrid>();

            foreach (var grid in grids)
            {
                if (IsSquareImage(grid.Url))
                {
                    squareGrids.Add(grid);
                }
            }

            return squareGrids;
        }

        /// <summary>
        /// Check if an image URL represents a square image.
        /// Parses dimensions from URL pattern (e.g., "/grid/512x512/...").
        /// </summary>
        private bool IsSquareImage(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            try
            {
                // URL pattern: https://cdn2.steamgriddb.com/grid/512x512/abc123.png
                // or: https://cdn2.steamgriddb.com/thumb/512x512/abc123.png
                var parts = url.Split('/');

                // Find the dimensions part (e.g., "512x512")
                foreach (var part in parts)
                {
                    if (part.Contains("x"))
                    {
                        var dimensions = part.Split('x');

                        if (dimensions.Length == 2)
                        {
                            if (int.TryParse(dimensions[0], out int width) && int.TryParse(dimensions[1], out int height))
                            {
                                return width == height;
                            }
                        }
                    }
                }
            }
            catch
            {
                // If parsing fails, assume not square
            }

            return false;
        }

        /// <summary>
        /// Generic GET request helper.
        /// </summary>
        private async Task<T> GetAsync<T>(string url, CancellationToken cancellationToken) where T : class
        {
            try
            {
                var uri = new Uri(url);

                // Create a linked cancellation token source for timeout
                using (var timeoutCts = new System.Threading.CancellationTokenSource(timeout))
                using (var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                {
                    var response = await httpClient.GetAsync(uri).AsTask(linkedCts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync().AsTask(linkedCts.Token);
                        return DeserializeJson<T>(content);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"SteamGridDB API error: {response.StatusCode}");
                    }
                }
            }
            catch (TaskCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    System.Diagnostics.Debug.WriteLine("SteamGridDB API request cancelled by user");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"SteamGridDB API request timed out after {timeout.TotalSeconds} seconds");
                }
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SteamGridDB API exception: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Deserialises JSON to object using DataContractJsonSerializer.
        /// </summary>
        private T DeserializeJson<T>(string json) where T : class
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return serializer.ReadObject(stream) as T;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JSON deserialization error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Releases the resources used by the current instance of the class.
        /// </summary>
        public void Dispose()
        {
            if (!disposed)
            {
                httpClient?.Dispose();
                disposed = true;
            }
        }
    }
}
