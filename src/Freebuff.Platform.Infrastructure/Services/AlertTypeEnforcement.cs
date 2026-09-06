using Microsoft.EntityFrameworkCore;
using Freebuff.Platform.Infrastructure.Data;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>
/// Checks whether a company is entitled to receive a given alert type before
/// the alert pipeline fires it. An alert type a company isn't entitled to must
/// never generate an alert record — not even a suppressed/hidden one.
/// </summary>
public interface IAlertTypeEnforcement
{
    /// <summary>Returns true if the company is entitled to receive alerts of this code.</summary>
    Task<bool> IsEntitledAsync(Guid companyId, string alertTypeCode);
}

public class AlertTypeEnforcement : IAlertTypeEnforcement
{
    private readonly ApplicationDbContext _db;

    public AlertTypeEnforcement(ApplicationDbContext db) => _db = db;

    public async Task<bool> IsEntitledAsync(Guid companyId, string alertTypeCode)
    {
        return await _db.CompanyAlertSubscriptions
            .AsNoTracking()
            .AnyAsync(s => s.CompanyId == companyId
                && s.AlertType.Code == alertTypeCode
                && s.Enabled
                && !s.IsDeleted
                && !s.AlertType.IsDeleted);
    }
}
