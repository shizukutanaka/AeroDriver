using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Models;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core.Services
{
    /// <summary>
    /// Windows Update Catalogと連携してWHQL認証ドライバーを検索・ダウンロードするサービス
    /// </summary>
    public class WhqlDatabaseService : IWhqlDatabaseService
    {
        private readonly ILogger<WhqlDatabaseService> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _cacheDirectory;
        
        // Windows Update Catalog URL
        private const string CATALOG_BASE_URL = "https://www.catalog.update.microsoft.com";
        private const string CATALOG_SEARCH_URL = CATALOG_BASE_URL + "/Search.aspx";
        private const string CATALOG_DOWNLOAD_URL = CATALOG_BASE_URL + "/DownloadDialog.aspx";
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        public WhqlDatabaseService(ILogger<WhqlDatabaseService> logger)
        {
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "AeroDriver/1.0");
            
            // キャッシュディレクトリの作成
            _cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AeroDriver",
                "WHQLCache");
            
            if (!Directory.Exists(_cacheDirectory))
            {
                Directory.CreateDirectory(_cacheDirectory);
            }
        }
        
        /// <summary>
        /// ドライバー情報に基づいて利用可能な更新を検索します
        /// </summary>
        public async Task<DriverInfo> FindAvailableUpdateAsync(DriverInfo currentDriver)
        {
            if (currentDriver == null)
                return null;

            try
            {
                _logger.LogInformation("ドライバー更新を検索中: {DeviceName}", currentDriver.DeviceName);
                
                // ハードウェアIDを使用してドライバーを検索
                var updatedDriver = await FindDriverByHardwareIdAsync(currentDriver.HardwareID);
                
                if (updatedDriver == null)
                    return null;

                // 新しいバージョンかどうかチェック
                if (Utilities.VersionHelper.IsNewer(updatedDriver.DriverVersion, currentDriver.DriverVersion))
                {
                    _logger.LogInformation("新しいドライバーが見つかりました: {DeviceName} {OldVersion} -> {NewVersion}", 
                        currentDriver.DeviceName, currentDriver.DriverVersion, updatedDriver.DriverVersion);
                    return updatedDriver;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバー更新検索エラー: {DeviceName}", currentDriver.DeviceName);
                return null;
            }
        }

        /// <summary>
        /// ハードウェアIDに基づいてドライバーを検索します
        /// </summary>
        public async Task<DriverInfo> FindDriverByHardwareIdAsync(string hardwareId)
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
                string vendorId = null;
                string deviceId = null;
                
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
                    .ThenByDescending(d => Utilities.VersionHelper.Compare(d.DriverVersion, "0.0.0.0"))
                    .FirstOrDefault();
                
                // ダウンロードリンクを取得
                if (latestDriver != null)
                {
                    string downloadUrl = await GetDownloadLinkAsync(latestDriver.Id);
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
        private async Task<string> GetDownloadLinkAsync(string driverId)
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
                    string downloadLink = match.Groups[1].Value;
                    
                    // インストーラータイプを判断
                    string installerType = "inf"; // デフォルト
                    
                    if (downloadLink.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        installerType = "exe";
                    }
                    else if (downloadLink.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                    {
                        installerType = "msi";
                    }
                    else if (downloadLink.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                             downloadLink.EndsWith(".cab", StringComparison.OrdinalIgnoreCase))
                    {
                        installerType = "zip";
                    }
                    
                    return downloadLink;
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
        private DriverInfo CheckCache(string hardwareId)
        {
            try
            {
                string cacheFile = Path.Combine(_cacheDirectory, $"{GetSafeFileName(hardwareId)}.json");
                
                if (!File.Exists(cacheFile))
                {
                    return null;
                }
                
                string json = File.ReadAllText(cacheFile);
                var cachedInfo = JsonSerializer.Deserialize<CachedDriverInfo>(json);
                
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
                
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(cachedInfo, options);
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
        public async Task<string> GetVendorIdByNameAsync(string vendorName)
        {
            try
            {
                _logger.LogInformation("製造元名 {VendorName} のベンダーIDを取得しています", vendorName);
                
                // よく知られたベンダーのマッピング
                var knownVendors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "nvidia", "10DE" },
                    { "amd", "1002" },
                    { "ati", "1002" },
                    { "advanced micro devices", "1002" },
                    { "intel", "8086" },
                    { "realtek", "10EC" },
                    { "broadcom", "14E4" },
                    { "qualcomm", "168C" },
                    { "marvell", "11AB" },
                    { "via", "1106" },
                    { "asus", "1043" },
                    { "gigabyte", "1458" },
                    { "msi", "1462" },
                    { "hp", "103C" },
                    { "dell", "1028" },
                    { "lenovo", "17AA" },
                    { "toshiba", "1179" },
                    { "samsung", "144D" }
                };
                
                // 名前でマッチングを試行
                foreach (var vendor in knownVendors)
                {
                    if (vendorName.Contains(vendor.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        return vendor.Value;
                    }
                }
                
                // オンラインのPCI IDデータベースを参照する場合は
                // ここに実装を追加
                
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
                _logger.LogInformation("WHQL認証ドライバーデータベースを更新しています");
                
                // キャッシュディレクトリをクリア
                Directory.GetFiles(_cacheDirectory, "*.json").ToList().ForEach(File.Delete);
                
                _logger.LogInformation("WHQL認証ドライバーデータベースのキャッシュをクリアしました");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WHQL認証ドライバーデータベース更新中にエラーが発生しました");
                return false;
            }
        }
    }
    
    /// <summary>
    /// キャッシュに保存するドライバー情報
    /// </summary>
    internal class CachedDriverInfo
    {
        /// <summary>
        /// ハードウェアID
        /// </summary>
        public string HardwareId { get; set; }
        
        /// <summary>
        /// ドライバー情報
        /// </summary>
        public DriverInfo DriverInfo { get; set; }
        
        /// <summary>
        /// キャッシュ時刻
        /// </summary>
        public DateTime CacheTime { get; set; }
    }
}