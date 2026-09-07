using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

public sealed record SpeedPolicy(double? MaxKmh);
public sealed record TyrePolicy(double? MinBar, double? MaxBar);

/// <summary>
/// Fleet sensor policies (speed limit, tyre pressure range). The company-wide
/// default lives in Configuration rows (Scope=Company, fleet.speed_policy_max_kmh
/// etc.); a per-vehicle override on the Vehicle row wins when set. The same
/// resolution serves the alert producer, the vehicle sensors endpoint and the
/// dashboard health widget, so the effective policy can never differ by surface.
/// </summary>
public class FleetPolicyService
{
    public const string SpeedPolicyKey = "fleet.speed_policy_max_kmh";
    public const string TyreMinPolicyKey = "fleet.tyre_pressure_min_bar";
    public const string TyreMaxPolicyKey = "fleet.tyre_pressure_max_bar";

    private readonly ApplicationDbContext _db;

    public FleetPolicyService(ApplicationDbContext db) => _db = db;

    public async Task<SpeedPolicy> GetSpeedPolicyAsync(Guid companyId, Vehicle vehicle)
    {
        var max = vehicle.SpeedPolicyMaxKmh ?? await CompanyDoubleAsync(companyId, SpeedPolicyKey);
        return new SpeedPolicy(max);
    }

    public async Task<TyrePolicy> GetTyrePolicyAsync(Guid companyId, Vehicle vehicle)
    {
        var min = vehicle.TyrePressureMinBar ?? await CompanyDoubleAsync(companyId, TyreMinPolicyKey);
        var max = vehicle.TyrePressureMaxBar ?? await CompanyDoubleAsync(companyId, TyreMaxPolicyKey);
        return new TyrePolicy(min, max);
    }

    /// <summary>Upserts a company fleet-policy Configuration row (Settings → Fleet Policies).</summary>
    public async Task SetCompanyValueAsync(Guid companyId, string key, double? value)
    {
        var row = await _db.Configurations.FirstOrDefaultAsync(c => c.Key == key
            && c.Scope == ConfigurationScope.Company && !c.IsDeleted
            && (c.ScopeEntityId == companyId || c.CompanyId == companyId));
        if (row == null)
        {
            _db.Configurations.Add(new Configuration
            {
                Id = Guid.NewGuid(),
                Key = key,
                Value = value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                ValueType = ConfigurationValueType.Number,
                Scope = ConfigurationScope.Company,
                ScopeEntityId = companyId,
                CompanyId = companyId,
                Module = "fleet",
                Description = "Fleet sensor policy default (Settings → Fleet Policies)"
            });
        }
        else
        {
            row.Value = value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }

    private async Task<double?> CompanyDoubleAsync(Guid companyId, string key)
    {
        var raw = await _db.Configurations.AsNoTracking()
            .Where(c => c.Key == key && c.Scope == ConfigurationScope.Company && !c.IsDeleted
                && (c.ScopeEntityId == companyId || c.CompanyId == companyId))
            .Select(c => c.Value)
            .FirstOrDefaultAsync();
        return double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}