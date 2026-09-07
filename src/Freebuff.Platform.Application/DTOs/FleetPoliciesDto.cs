namespace Freebuff.Platform.Application.DTOs;

/// <summary>Fleet-wide sensor policy defaults (Settings → Fleet Policies). Null = leave unchanged; 0 = clear back to unconfigured.</summary>
public class FleetPoliciesDto
{
    /// <summary>Company-wide default max speed policy (km/h); per-vehicle override wins when set.</summary>
    public double? SpeedPolicyMaxKmh { get; set; }

    /// <summary>Company-wide minimum tyre pressure (bar).</summary>
    public double? TyrePressureMinBar { get; set; }

    /// <summary>Company-wide maximum tyre pressure (bar).</summary>
    public double? TyrePressureMaxBar { get; set; }
}