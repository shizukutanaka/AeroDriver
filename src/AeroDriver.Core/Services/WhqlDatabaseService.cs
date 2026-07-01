using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using AeroDriver.Core.Helpers;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Services
{
    /// <summary>
    /// Windows Update Catalogと連携してWHQL認証ドライバーを検索・ダウンロードするサービス
    /// </summary>
    public class WhqlDatabaseService : IWhqlDatabaseService
    {
        private readonly ILogger<WhqlDatabaseService> _logger;
        private readonly HttpClient _httpClient;
        private readonly PciIdDatabase _pciIds;
        private readonly string _cacheDirectory;

        private const string CATALOG_BASE_URL = "https://www.catalog.update.microsoft.com";
        private const string CATALOG_SEARCH_URL = CATALOG_BASE_URL + "/Search.aspx";
        private const string CATALOG_DOWNLOAD_URL = CATALOG_BASE_URL + "/DownloadDialog.aspx";

        public WhqlDatabaseService(
            ILogger<WhqlDatabaseService> logger,
            PciIdDatabase pciIds,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pciIds = pciIds ?? throw new ArgumentNullException(nameof(pciIds));
            // IHttpClientFactory: 接続プール管理・SocketsHttpHandler の再利用・レジリエンスハンドラー適用
            _httpClient = (httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory)))
                .CreateClient(nameof(WhqlDatabaseService));
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent", "AeroDriver/1.0 (Windows Driver Manager)");

            _cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AeroDriver", "WHQLCache");
            Directory.CreateDirectory(_cacheDirectory);
        }
        
        /// <summary>
        /// ハードウェアIDに基づいてドライバーを検索します
        /// </summary>
        public async Task<DriverInfo?> FindDriverByHardwareIdAsync(string hardwareId)
        {
            try
            {
                _logger.LogInformation("ハードウェアID {HardwareId} のドライバーを検索しています", hardwareId);
                
                // キャッシュを確認
                var cachedDriver = CheckCache(hardwareId);
                if (cachedDriver != null)
                {
                    _logger.LogInformation("キャッシュから {HardwareId} のドライバー情報を取得しました", hardwareId);
                    return cachedDriver;
                }
                
                // ハードウェアIDからベンダーIDとデバイスIDを抽出
                string? vendorId = null;
                string? deviceId = null;
                
                var venMatch = Regex.Match(hardwareId, @"VEN_([0-9A-F]{4})", RegexOptions.IgnoreCase);
                if (venMatch.Success)
                {
                    vendorId = venMatch.Groups[1].Value;
                }
                
                var devMatch = Regex.Match(hardwareId, @"DEV_([0-9A-F]{4})", RegexOptions.IgnoreCase);
                if (devMatch.Success)
                {
                    deviceId = devMatch.Groups[1].Value;
                }
                
                if (string.IsNullOrEmpty(vendorId) || string.IsNullOrEmpty(deviceId))
                {
                    _logger.LogWarning("ハードウェアID {HardwareId} からVEN/DEVコードを抽出できませんでした", hardwareId);
                    return null;
                }
                
                // 検索クエリの構築
                string searchQuery = $"PCI\\VEN_{vendorId}&DEV_{deviceId}";
                
                // Windows Update Catalogから検索
                var searchResults = await SearchCatalogAsync(searchQuery);
                if (searchResults.Count == 0)
                {
                    _logger.LogInformation("ハードウェアID {HardwareId} に一致するドライバーが見つかりませんでした", hardwareId);
                    return null;
                }
                
                // 最新のドライバーを選択
                var latestDriver = searchResults
                    .OrderByDescending(d => d.DriverDate)
                    .ThenByDescending(d => d.DriverVersion, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                
                // ダウンロードリンクを取得
                if (latestDriver != null)
                {
                    string? downloadUrl = await GetDownloadLinkAsync(latestDriver.Id);
                    if (!string.IsNullOrEmpty(downloadUrl))
                    {
                        latestDriver.DownloadUrl = downloadUrl;
                        
                        // キャッシュに保存
                        SaveToCache(hardwareId, latestDriver);
                        
                        _logger.LogInformation("ハードウェアID {HardwareId} のドライバーが見つかりました: {DriverVersion}", hardwareId, latestDriver.DriverVersion);
                        return latestDriver;
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ハードウェアID {HardwareId} のドライバー検索中にエラーが発生しました", hardwareId);
                return null;
            }
        }
        
        /// <summary>
        /// Windows Update Catalogを検索します
        /// </summary>
        private async Task<List<DriverInfo>> SearchCatalogAsync(string query)
        {
            try
            {
                _logger.LogInformation("Windows Update Catalogを検索しています: {Query}", query);
                
                var results = new List<DriverInfo>();
                
                // 検索ページを取得
                var formContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("q", query),
                    new KeyValuePair<string, string>("driverClass", "ALL")
                });
                
                var response = await _httpClient.PostAsync(CATALOG_SEARCH_URL, formContent);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                
                // 検索結果からドライバー情報を抽出
                // 実際のWebサイトはHTMLスクレイピングになるため、
                // ここでは正規表現を使用して必要な情報を抽出します
                
                // 各ドライバーエントリのパターン
                var driverPattern = @"<div class=""driver-item""[^>]*>.*?<a[^>]*id=""([^""]*)"">([^<]*)</a>.*?<div class=""driver-version"">([^<]*)</div>.*?<div class=""driver-date"">([^<]*)</div>.*?</div>";
                var matches = Regex.Matches(content, driverPattern, RegexOptions.Singleline);
                
                foreach (Match match in matches)
                {
                    if (match.Groups.Count >= 5)
                    {
                        var driver = new DriverInfo
                        {
                            Id = match.Groups[1].Value,
                            DeviceName = HttpUtility.HtmlDecode(match.Groups[2].Value.Trim()),
                            DriverVersion = match.Groups[3].Value.Trim(),
                            IsWHQLCertified = true // Windows Update CatalogのドライバーはすべてWHQL認証済み
                        };
                        
                        // 日付の解析
                        if (DateTime.TryParse(match.Groups[4].Value.Trim(), out DateTime driverDate))
                        {
                            driver.DriverDate = driverDate;
                        }
                        
                        // ベンダー名の抽出
                        var vendorMatch = Regex.Match(driver.DeviceName, @"^(.*?)\s+");
                        if (vendorMatch.Success)
                        {
                            driver.DriverProviderName = vendorMatch.Groups[1].Value.Trim();
                        }
                        
                        results.Add(driver);
                    }
                }
                
                _logger.LogInformation("{Count}個のドライバーが見つかりました", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Windows Update Catalog検索中にエラーが発生しました: {Query}", query);
                return new List<DriverInfo>();
            }
        }
        
        /// <summary>
        /// ドライバーのダウンロードリンクを取得します
        /// </summary>
        private async Task<string?> GetDownloadLinkAsync(string driverId)
        {
            try
            {
                _logger.LogInformation("ドライバーID {DriverId} のダウンロードリンクを取得しています", driverId);
                
                // ダウンロードダイアログを取得
                var formContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("id", driverId)
                });
                
                var response = await _httpClient.PostAsync(CATALOG_DOWNLOAD_URL, formContent);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                
                // ダウンロードリンクを抽出
                var linkPattern = @"downloadInformation\[\d+\]\.url\s*=\s*'([^']*)'";
                var match = Regex.Match(content, linkPattern);
                
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
                
                _logger.LogWarning("ドライバーID {DriverId} のダウンロードリンクが見つかりませんでした", driverId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバーID {DriverId} のダウンロードリンク取得中にエラーが発生しました", driverId);
                return null;
            }
        }
        
        /// <summary>
        /// キャッシュからドライバー情報を取得します
        /// </summary>
        private DriverInfo? CheckCache(string hardwareId)
        {
            try
            {
                string cacheFile = Path.Combine(_cacheDirectory, $"{GetSafeFileName(hardwareId)}.json");

                if (!File.Exists(cacheFile))
                {
                    return null;
                }

                string json = File.ReadAllText(cacheFile);
                var cachedInfo = JsonConvert.DeserializeObject<CachedDriverInfo>(json);

                // 破損・空JSON("null"等)の場合、DeserializeObject は null を返しうる。
                // 未チェックで cachedInfo.CacheTime にアクセスすると NullReferenceException になる。
                if (cachedInfo == null)
                {
                    _logger.LogWarning("キャッシュファイルが破損しています: {HardwareId}", hardwareId);
                    return null;
                }

                // キャッシュの有効期限をチェック (24時間)
                if (cachedInfo.CacheTime.AddHours(24) < DateTime.Now)
                {
                    _logger.LogInformation("キャッシュの有効期限が切れています: {HardwareId}", hardwareId);
                    return null;
                }

                return cachedInfo.DriverInfo;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "キャッシュ確認中にエラーが発生しました: {HardwareId}", hardwareId);
                return null;
            }
        }
        
        /// <summary>
        /// ドライバー情報をキャッシュに保存します
        /// </summary>
        private void SaveToCache(string hardwareId, DriverInfo driverInfo)
        {
            try
            {
                string cacheFile = Path.Combine(_cacheDirectory, $"{GetSafeFileName(hardwareId)}.json");
                
                var cachedInfo = new CachedDriverInfo
                {
                    HardwareId = hardwareId,
                    DriverInfo = driverInfo,
                    CacheTime = DateTime.Now
                };
                
                string json = JsonConvert.SerializeObject(cachedInfo, Formatting.Indented);
                File.WriteAllText(cacheFile, json);
                
                _logger.LogInformation("ドライバー情報をキャッシュに保存しました: {HardwareId}", hardwareId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "キャッシュ保存中にエラーが発生しました: {HardwareId}", hardwareId);
            }
        }
        
        /// <summary>
        /// ファイル名に使用できない文字を置き換えます
        /// </summary>
        private string GetSafeFileName(string input)
        {
            string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            string invalidReStr = string.Format(@"[{0}]", invalidChars);
            
            return Regex.Replace(input, invalidReStr, "_");
        }
        
        /// <summary>
        /// 製造元名からベンダーIDを取得します
        /// </summary>
        public async Task<string?> GetVendorIdByNameAsync(string vendorName)
        {
            try
            {
                _logger.LogInformation("製造元名 {VendorName} のベンダーIDを取得しています", vendorName);

                // PCI IDs データベース（50,000+ エントリ）で逆引き
                var id = await _pciIds.GetVendorIdByNameAsync(vendorName);
                if (id != null) return id;

                _logger.LogWarning("製造元名 {VendorName} のベンダーIDが見つかりませんでした", vendorName);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "製造元名 {VendorName} のベンダーID取得中にエラーが発生しました", vendorName);
                return null;
            }
        }
        
        /// <summary>
        /// WHQL認証ドライバーデータベースを更新します
        /// </summary>
        public async Task<bool> UpdateDriverDatabaseAsync()
        {
            try
            {
                _logger.LogInformation("ドライバーデータベースを更新しています");

                // WHQLキャッシュをクリア
                foreach (var f in Directory.GetFiles(_cacheDirectory, "*.json"))
                    File.Delete(f);

                // PCI IDs データベースを最新化
                await _pciIds.RefreshAsync();

                _logger.LogInformation("ドライバーデータベースの更新が完了しました");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバーデータベース更新中にエラーが発生しました");
                return false;
            }
        }
    }
    
    /// <summary>
    /// キャッシュに保存するドライバー情報
    /// </summary>
    internal class CachedDriverInfo
    {
        public string? HardwareId { get; set; }
        public DriverInfo? DriverInfo { get; set; }
        public DateTime CacheTime { get; set; }
    }
}