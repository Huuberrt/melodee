using Melodee.Common.Enums;
using Melodee.Common.Utility;

namespace Melodee.Common.Models;

public sealed record Statistic(
    StatisticType Type,
    string Title,
    object Data,
    string? DisplayColor,
    string? Message = null,
    short? SortOrder = null,
    string? Icon = null,
    bool? IncludeInApiResult = null,
    StatisticCategory? Category = StatisticCategory.NotSet)
{
    public int? DataAsInt => SafeParser.ToNumber<int>(Data);
    
    public long? DataAsLong => SafeParser.ToNumber<long>(Data);
}
