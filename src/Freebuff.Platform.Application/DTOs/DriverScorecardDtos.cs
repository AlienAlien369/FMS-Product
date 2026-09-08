namespace Freebuff.Platform.Application.DTOs;

/// <summary>Effective composite weights + where they came from (GET /scorecard-config response).</summary>
public class ScoreWeightConfigDto
{
    public double Safety { get; set; }
    public double Compliance { get; set; }
    public double Punctuality { get; set; }
    public double Behavior { get; set; }

    /// <summary>"company" = this company's override; "platform" = platform default.</summary>
    public string Source { get; set; } = "platform";
}

/// <summary>Wire shape for PUT /scorecard-config (flat weight names + companyId in the body).</summary>
public class ScoreWeightConfigUpdateDto
{
    public double SafetyWeight { get; set; }
    public double ComplianceWeight { get; set; }
    public double PunctualityWeight { get; set; }
    public double BehaviorWeight { get; set; }

    /// <summary>Target company for the override; absent = platform default (SuperAdmin only).</summary>
    public Guid? CompanyId { get; set; }
}

/// <summary>One driver's materialized scorecard for a window (with trend + explainability feed).</summary>
public class DriverScorecardDto
{
    public Guid DriverId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Window { get; set; } = "30d";

    public bool InsufficientData { get; set; }
    public int TripCount { get; set; }
    public int EventCount { get; set; }
    public decimal? DistanceKm { get; set; }

    public ScoreWeightConfigDto Weights { get; set; } = new();

    /// <summary>Null category values = insufficient data.</summary>
    public DriverScorecardScoresDto Scores { get; set; } = new();

    /// <summary>Recent anchors for this window (trend, oldest → newest).</summary>
    public List<DriverScoreTrendPointDto> Trend { get; set; } = new();

    /// <summary>The events that most affected this period (explainability — a low score must be explainable in a coaching conversation).</summary>
    public List<DriverScoreEventDto> TopEvents { get; set; } = new();
}

public class DriverScorecardScoresDto
{
    public decimal? Safety { get; set; }
    public decimal? Compliance { get; set; }
    public decimal? Punctuality { get; set; }
    public decimal? Behavior { get; set; }
    public decimal? Composite { get; set; }
}

public class DriverScoreTrendPointDto
{
    public DateTime AnchorDate { get; set; }
    public decimal? Composite { get; set; }
    public decimal? Safety { get; set; }
}

public class DriverScoreEventDto
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string EventTypeName { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public DateTime EventTimeUtc { get; set; }
    public string? MediaUrl { get; set; }
    public double Deduction { get; set; }
}

/// <summary>One row of the fleet ranking / leaderboard for a window.</summary>
public class DriverScoreRankingDto
{
    public Guid DriverId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal? Composite { get; set; }
    public decimal? Safety { get; set; }
    public decimal? Compliance { get; set; }
    public decimal? Punctuality { get; set; }
    public decimal? Behavior { get; set; }
    public bool InsufficientData { get; set; }
    public int TripCount { get; set; }
}

/// <summary>Dashboard widget aggregate over the fleet's latest 30-day periods.</summary>
public class DriverScoreOverviewDto
{
    public decimal Average { get; set; }
    public int BelowThresholdCount { get; set; }
    public int Threshold { get; set; } = 40;
    public int InsufficientDataCount { get; set; }
    public List<ScoreBucketDto> Distribution { get; set; } = new();
}

public class ScoreBucketDto
{
    public string Bucket { get; set; } = string.Empty; // "0-20" | "21-40" | ...
    public int Count { get; set; }
}