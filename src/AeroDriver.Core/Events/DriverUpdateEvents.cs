using System;
using System.Collections.Generic;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Events
{
    /// <summary>
    /// ドライバー更新が利用可能な場合のイベント引数
    /// </summary>
    public class UpdatesAvailableEventArgs : EventArgs
    {
        /// <summary>
        /// 利用可能なドライバー更新のリスト
        /// </summary>
        public List<DriverInfo> AvailableUpdates { get; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public UpdatesAvailableEventArgs(List<DriverInfo> availableUpdates)
        {
            AvailableUpdates = availableUpdates ?? throw new ArgumentNullException(nameof(availableUpdates));
        }
    }

    /// <summary>
    /// ドライバーがインストールされた場合のイベント引数
    /// </summary>
    public class UpdatesInstalledEventArgs : EventArgs
    {
        /// <summary>
        /// インストールされたドライバー情報
        /// </summary>
        public DriverInfo InstalledDriver { get; }

        /// <summary>
        /// インストールが成功したかどうか
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// エラーメッセージ（失敗した場合）
        /// </summary>
        public string ErrorMessage { get; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public UpdatesInstalledEventArgs(DriverInfo installedDriver, bool isSuccess, string errorMessage = null)
        {
            InstalledDriver = installedDriver;
            IsSuccess = isSuccess;
            ErrorMessage = errorMessage;
        }
    }
}
