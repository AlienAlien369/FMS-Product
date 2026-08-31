using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Company/Tenant - the top-level tenant entity.
/// Everything belongs to a company except platform-level data.
/// </summary>
public class Company : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Website { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? PostalCode { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;

    // Defaults
    public string DefaultLanguage { get; set; } = "en";
    public string DefaultTimezone { get; set; } = "UTC";
    public string DefaultCurrency { get; set; } = "USD";
    public string DateFormat { get; set; } = "yyyy-MM-dd";
    public string TimeFormat { get; set; } = "HH:mm";
    public string NumberFormat { get; set; } = "en-US";
    public MapProvider DefaultMapProvider { get; set; } = MapProvider.OpenStreetMap;
    public string? MapApiKey { get; set; }

    // Subscription
    public Guid? SubscriptionId { get; set; }
    public Guid? PackageId { get; set; }

    // Navigation
    public Subscription? Subscription { get; set; }
    public Package? Package { get; set; }
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
    public ICollection<Driver> Drivers { get; set; } = new List<Driver>();
    public ICollection<Client> Clients { get; set; } = new List<Client>();
    public ICollection<Geofence> Geofences { get; set; } = new List<Geofence>();
    public ICollection<Trip> Trips { get; set; } = new List<Trip>();
    public ICollection<Role> Roles { get; set; } = new List<Role>();
    public ICollection<Configuration> Configurations { get; set; } = new List<Configuration>();
    public ICollection<AlertConfiguration> AlertConfigurations { get; set; } = new List<AlertConfiguration>();
    public ICollection<ModuleConfiguration> EnabledModules { get; set; } = new List<ModuleConfiguration>();
}
