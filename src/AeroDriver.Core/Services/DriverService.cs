using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Services
{
    /// <summary>
    /// ドライバーの検出、更新、インストールを管理するサービス
    /// </summary>
    public class DriverService : IDriverService, IDisposable
    {
        private readonly ILogger<DriverService> _logger;
        private readonly ISettingsService _settingsService;
        private readonly IBackupService _backupService;
        private readonly IWhqlDatabaseService _whqlDatabaseService;
        private readonly INotificationService _notificationService;
        private readonly HttpClient _httpClient;
        private bool _disposed = false;

        /// <summary>
        /// 新しいインスタンスを初期化します
        /// </summary>
        public DriverService(
            ILogger<DriverService> logger,
            ISettingsService settingsService,
            IBackupService backupService,
            IWhqlDatabaseService whqlDatabaseService,
            INotificationService notificationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
            _whqlDatabaseService = whqlDatabaseService ?? throw new ArgumentNullException(nameof(whqlDatabaseService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// バージョン文字列を比較します
        /// </summary>
        public int CompareVersions(string version1, string version2)
        {
            if (string.IsNullOrEmpty(version1) && string.IsNullOrEmpty(version2))
                return 0;
            if (string.IsNullOrEmpty(version1))
                return -1;
            if (string.IsNullOrEmpty(version2))
                return 1;

            try
            {
                // バージョン文字列を分割して比較
                string[] v1Parts = version1.Split('.', ',');
                string[] v2Parts = version2.Split('.', ',');
                
                int maxLength = Math.Max(v1Parts.Length, v2Parts.Length);
                
                for (int i = 0; i < maxLength; i++)
                {
                    int v1Value = i < v1Parts.Length && int.TryParse(v1Parts[i], out int temp1) ? temp1 : 0;
                    int v2Value = i < v2Parts.Length && int.TryParse(v2Parts[i], out int temp2) ? temp2 : 0;
                    
                    if (v1Value > v2Value)
                        return 1;
                    if (v1Value < v2Value)
                        return -1;
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "バージョン比較中にエラーが発生しました: {Version1} vs {Version2}", version1, version2);
                return 0;
            }
        }


        /// <summary>
        /// ドライバーをロールバックします
        /// </summary>
        public async Task<bool> RollbackDriverAsync(string deviceId)
        {
            try
            {
                _logger.LogInformation("ドライバーをロールバックします: {DeviceID}", deviceId);
                
                // バックアップリストを取得
                var backups = await _backupService.GetBackupsAsync();
                
                if (backups.Count == 0)
                {
                    _logger.LogWarning("利用可能なバックアップがありません");
                    return false;
                }
                
                // 最新のバックアップを使用
                var latestBackup = backups.OrderByDescending(b => b.CreationDate).First();
                
                // バックアップから復元
                bool result = await _backupService.RestoreBackupAsync(latestBackup.Id);
                
                if (result)
                {
                    _logger.LogInformation("ドライバーのロールバックが成功しました: {DeviceID}", deviceId);
                    
                    // 通知を送信
                    _notificationService.SendNotification(
                        "ドライバーロールバック完了",
                        "ドライバーが正常に以前のバージョンに戻されました。",
                        NotificationType.Success);
                }
                else
                {
                    _logger.LogError("ドライバーのロールバックに失敗しました: {DeviceID}", deviceId);
                    
                    // 通知を送信
                    _notificationService.SendNotification(
                        "ドライバーロールバック失敗",
                        "ドライバーを以前のバージョンに戻せませんでした。",
                        NotificationType.Error);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバーのロールバック中にエラーが発生しました: {DeviceID}", deviceId);
                return false;
            }
        }

        /// <summary>
        /// リソースを解放します
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _httpClient?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}
