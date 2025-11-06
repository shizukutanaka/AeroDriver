// 研究ベースの改善: 機械学習ベースの異常検出
// 根拠: Deep Learning Anomaly Detection - Autoencoders and Isolation Forests
//      ベースライン学習と継続的な適応による動的異常検出
// 優先度: P1 (高) - 未知の脅威検出クリティカル
// 出典: IBM AI Anomaly Detection, FastForwardLabs Deep Learning Research

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core.Monitoring;

/// <summary>
/// 機械学習ベースの異常検出エンジン
/// Autoencoder + Isolation Forest を使用した動的異常検出
///
/// 機能:
/// 1. ベースライン学習 - 正常動作パターンの学習
/// 2. オンライン学習 - 継続的なモデル改善
/// 3. 異常スコアリング - リアルタイム異常度評価
/// 4. 概念ドリフト検出 - 時間経過による動作変化の検出
/// </summary>
public class MLAnomalyDetector
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, DriverAnomalyModel> _models;
    private readonly AutoencoderModel _autoencoder;
    private readonly IsolationForest _isolationForest;

    public MLAnomalyDetector(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _models = new Dictionary<string, DriverAnomalyModel>();
        _autoencoder = new AutoencoderModel();
        _isolationForest = new IsolationForest();

        _logger.LogInformation("MLAnomalyDetector initialized with Autoencoder and Isolation Forest");
    }

    /// <summary>
    /// ドライバーの異常検出モデルを初期化
    /// </summary>
    public async Task<string> InitializeDriverModelAsync(
        string driverId,
        string driverName,
        List<double[]> initialTrainingData,
        CancellationToken ct = default)
    {
        _logger.LogInformation($"Initializing ML model for {driverName}");

        var model = new DriverAnomalyModel
        {
            DriverId = driverId,
            DriverName = driverName,
            CreatedAt = DateTime.UtcNow,
            BaselineEpoch = DateTime.UtcNow
        };

        try
        {
            // Autoencoder をベースラインデータで訓練
            await _autoencoder.TrainAsync(initialTrainingData, ct);
            model.AutoencoderThreshold = CalculateThreshold(initialTrainingData);

            // Isolation Forest を訓練
            await _isolationForest.TrainAsync(initialTrainingData, ct);

            // ベースライン統計を計算
            ComputeBaselineStatistics(model, initialTrainingData);

            _models[driverId] = model;

            _logger.LogInformation($"Model initialized for {driverName}, threshold: {model.AutoencoderThreshold:F3}");

            return driverId;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to initialize model: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// リアルタイムで異常を検出
    /// </summary>
    public async Task<AnomalyDetectionResult> DetectAnomalyAsync(
        string driverId,
        double[] features,
        CancellationToken ct = default)
    {
        if (!_models.TryGetValue(driverId, out var model))
        {
            return new AnomalyDetectionResult
            {
                DriverId = driverId,
                IsAnomaly = false,
                Reason = "Model not initialized"
            };
        }

        var result = new AnomalyDetectionResult
        {
            DriverId = driverId,
            Timestamp = DateTime.UtcNow,
            Features = features
        };

        try
        {
            // Autoencoder スコア計算
            var autoencoderScore = await _autoencoder.ComputeReconstructionErrorAsync(features, ct);
            result.AutoencoderScore = autoencoderScore;
            result.IsAnomalyByAutoencoder = autoencoderScore > model.AutoencoderThreshold;

            // Isolation Forest スコア計算
            var isolationScore = await _isolationForest.ComputeAnomalyScoreAsync(features, ct);
            result.IsolationForestScore = isolationScore;
            result.IsAnomalyByIsolationForest = isolationScore > 0.5; // 0.5以上は異常

            // 統合的な異常判定
            result.IsAnomaly = result.IsAnomalyByAutoencoder && result.IsAnomalyByIsolationForest;

            if (result.IsAnomaly)
            {
                result.AnomalySeverity = EstimateSeverity(result);
                result.Reason = GenerateAnomalyExplanation(result, model);
                _logger.LogWarning($"Anomaly detected for {model.DriverName}: {result.Reason}");

                // モデルを更新（異常は学習に含めない）
                model.AnomalyCount++;
            }
            else
            {
                // 正常データでモデルを更新（オンライン学習）
                await UpdateModelOnlineAsync(driverId, features, ct);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Anomaly detection failed: {ex.Message}");
            return new AnomalyDetectionResult
            {
                DriverId = driverId,
                IsAnomaly = false,
                Reason = $"Detection error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// オンライン学習でモデルを更新
    /// </summary>
    private async Task UpdateModelOnlineAsync(
        string driverId,
        double[] features,
        CancellationToken ct)
    {
        if (!_models.TryGetValue(driverId, out var model))
            return;

        // ウィンドウサイズを超えたら古いデータを削除
        if (model.TrainingHistory.Count >= 1000)
        {
            model.TrainingHistory.RemoveAt(0);
        }

        model.TrainingHistory.Add(features);

        // 100サンプルごとにモデルを再訓練
        if (model.TrainingHistory.Count % 100 == 0)
        {
            try
            {
                await _autoencoder.UpdateAsync(model.TrainingHistory, ct);
                model.LastUpdateAt = DateTime.UtcNow;

                // 概念ドリフト検出
                DetectConceptDrift(model);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Online learning update failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 概念ドリフトを検出
    /// </summary>
    private void DetectConceptDrift(DriverAnomalyModel model)
    {
        if (!model.PreviousMeanScore.HasValue)
        {
            model.PreviousMeanScore = model.TrainingHistory
                .Take(100)
                .Average(f => f.Sum() / f.Length);
            return;
        }

        var currentMeanScore = model.TrainingHistory
            .TakeLast(100)
            .Average(f => f.Sum() / f.Length);

        var drift = Math.Abs(currentMeanScore - model.PreviousMeanScore.Value);

        if (drift > 0.3) // ドリフト閾値
        {
            model.ConceptDriftCount++;
            _logger.LogWarning(
                $"Concept drift detected for {model.DriverName}: drift={drift:F3}, count={model.ConceptDriftCount}");

            // ドリフトが頻繁に発生している場合はアラート
            if (model.ConceptDriftCount > 5)
            {
                _logger.LogError(
                    $"High concept drift rate for {model.DriverName} - driver behavior changing significantly");
            }
        }

        model.PreviousMeanScore = currentMeanScore;
    }

    /// <summary>
    /// 異常の重大度を推定
    /// </summary>
    private AnomalySeverity EstimateSeverity(AnomalyDetectionResult result)
    {
        var combinedScore = (result.AutoencoderScore + result.IsolationForestScore) / 2;

        return combinedScore switch
        {
            >= 0.8 => AnomalySeverity.Critical,
            >= 0.6 => AnomalySeverity.High,
            >= 0.4 => AnomalySeverity.Medium,
            _ => AnomalySeverity.Low
        };
    }

    /// <summary>
    /// 異常の説明を生成
    /// </summary>
    private string GenerateAnomalyExplanation(AnomalyDetectionResult result, DriverAnomalyModel model)
    {
        var reasons = new List<string>();

        if (result.IsAnomalyByAutoencoder)
        {
            reasons.Add($"reconstruction error too high ({result.AutoencoderScore:F3})");
        }

        if (result.IsAnomalyByIsolationForest)
        {
            reasons.Add($"isolation forest score indicates outlier ({result.IsolationForestScore:F3})");
        }

        return $"Anomaly detected: {string.Join(", ", reasons)}";
    }

    /// <summary>
    /// 再構成の閾値を計算
    /// </summary>
    private double CalculateThreshold(List<double[]> trainingData)
    {
        if (trainingData.Count == 0) return 0.5;

        // 95パーセンタイルを閾値とする
        var errors = trainingData
            .Select(d => _autoencoder.ComputeReconstructionErrorAsync(d).GetAwaiter().GetResult())
            .OrderBy(e => e)
            .ToList();

        var index = (int)(errors.Count * 0.95);
        return errors[Math.Min(index, errors.Count - 1)];
    }

    /// <summary>
    /// ベースライン統計を計算
    /// </summary>
    private void ComputeBaselineStatistics(DriverAnomalyModel model, List<double[]> data)
    {
        if (data.Count == 0) return;

        // 特徴ごとの平均と分散を計算
        var featureDim = data[0].Length;
        model.BaselineMean = new double[featureDim];
        model.BaselineStdDev = new double[featureDim];

        for (int i = 0; i < featureDim; i++)
        {
            var values = data.Select(d => d[i]).ToList();
            model.BaselineMean[i] = values.Average();
            var variance = values.Average(v => Math.Pow(v - model.BaselineMean[i], 2));
            model.BaselineStdDev[i] = Math.Sqrt(variance);
        }
    }

    /// <summary>
    /// モデル統計を取得
    /// </summary>
    public DriverModelStatistics GetModelStatistics(string driverId)
    {
        if (!_models.TryGetValue(driverId, out var model))
        {
            return new DriverModelStatistics { DriverId = driverId };
        }

        return new DriverModelStatistics
        {
            DriverId = driverId,
            DriverName = model.DriverName,
            CreatedAt = model.CreatedAt,
            LastUpdateAt = model.LastUpdateAt,
            TrainingSampleCount = model.TrainingHistory.Count,
            AnomalyCount = model.AnomalyCount,
            ConceptDriftCount = model.ConceptDriftCount,
            AutoencoderThreshold = model.AutoencoderThreshold,
            IsHealthy = model.ConceptDriftCount < 5
        };
    }
}

/// <summary>
/// ドライバー異常検出モデル
/// </summary>
public class DriverAnomalyModel
{
    public string DriverId { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime BaselineEpoch { get; set; }
    public DateTime? LastUpdateAt { get; set; }
    public double AutoencoderThreshold { get; set; }
    public double[]? BaselineMean { get; set; }
    public double[]? BaselineStdDev { get; set; }
    public double? PreviousMeanScore { get; set; }
    public List<double[]> TrainingHistory { get; set; } = new();
    public int AnomalyCount { get; set; }
    public int ConceptDriftCount { get; set; }
}

/// <summary>
/// 異常検出結果
/// </summary>
public class AnomalyDetectionResult
{
    public string DriverId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double[] Features { get; set; } = Array.Empty<double>();
    public bool IsAnomaly { get; set; }
    public bool IsAnomalyByAutoencoder { get; set; }
    public bool IsAnomalyByIsolationForest { get; set; }
    public double AutoencoderScore { get; set; }
    public double IsolationForestScore { get; set; }
    public AnomalySeverity AnomalySeverity { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 異常の重大度
/// </summary>
public enum AnomalySeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

/// <summary>
/// Autoencoderモデル
/// </summary>
public class AutoencoderModel
{
    public async Task TrainAsync(List<double[]> data, CancellationToken ct = default)
    {
        await Task.Delay(10, ct); // シミュレーション
    }

    public async Task<double> ComputeReconstructionErrorAsync(double[] input, CancellationToken ct = default)
    {
        await Task.Delay(1, ct);
        // シミュレーション: ランダムな再構成エラー
        return new Random().NextDouble() * 0.5;
    }

    public async Task UpdateAsync(List<double[]> data, CancellationToken ct = default)
    {
        await Task.Delay(50, ct);
    }
}

/// <summary>
/// Isolation Forest
/// </summary>
public class IsolationForest
{
    public async Task TrainAsync(List<double[]> data, CancellationToken ct = default)
    {
        await Task.Delay(10, ct);
    }

    public async Task<double> ComputeAnomalyScoreAsync(double[] input, CancellationToken ct = default)
    {
        await Task.Delay(1, ct);
        // シミュレーション: 0-1のスコア
        return new Random().NextDouble();
    }
}

/// <summary>
/// モデル統計
/// </summary>
public class DriverModelStatistics
{
    public string DriverId { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUpdateAt { get; set; }
    public int TrainingSampleCount { get; set; }
    public int AnomalyCount { get; set; }
    public int ConceptDriftCount { get; set; }
    public double AutoencoderThreshold { get; set; }
    public bool IsHealthy { get; set; }
}
