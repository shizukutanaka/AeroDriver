using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace AeroDriver.Service;

public sealed class StubAutoUpdateService : IAutoUpdateService
{
    private readonly ILogger<StubAutoUpdateService> _logger;
    private Timer? _timer;
    private int _intervalHours;

    public StubAutoUpdateService(ILogger<StubAutoUpdateService> logger)
    {
        _logger = logger;
    }

    public void StartAutoUpdateCheck(int intervalHours)
    {
        _intervalHours = Math.Max(1, intervalHours);
        _timer?.Dispose();
        _timer = new Timer(async _ => await CheckForUpdatesAsync(), null, TimeSpan.Zero, TimeSpan.FromHours(_intervalHours));
        _logger.LogInformation("Stub auto-update check started (interval: {Interval} hours)", _intervalHours);
    }

    public Task<AutoUpdateResult> CheckForUpdatesAsync()
    {
        // 実際の更新処理は未実装だが、ログのみ記録
        _logger.LogDebug("Stub auto-update check executed");
        return Task.FromResult(new AutoUpdateResult(false, "No updates available in stub implementation."));
    }

    public void StopAutoUpdateCheck()
    {
        _timer?.Dispose();
        _timer = null;
        _logger.LogInformation("Stub auto-update check stopped");
    }
}
