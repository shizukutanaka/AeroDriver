using System.Threading;
using System.Threading.Tasks;

namespace AeroDriver.Core.Interfaces;

public interface ISecurityService
{
    Task<SecurityReport> PerformSecurityAuditAsync(CancellationToken cancellationToken = default);
}
