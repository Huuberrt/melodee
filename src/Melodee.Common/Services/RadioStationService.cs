using Ardalis.GuardClauses;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Filtering;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;
using SmartFormat;
using System.Linq.Expressions;
using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Common.Services;

public class RadioStationService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    private const string CacheKeyDetailByApiKeyTemplate = "urn:radiostation:apikey:{0}";
    private const string CacheKeyDetailTemplate = "urn:radiostation:{0}";

    public async Task<MelodeeModels.PagedResult<RadioStation>> ListAsync(MelodeeModels.PagedRequest pagedRequest,
        CancellationToken cancellationToken = default)
    {
        int radioStationCount;
        RadioStation[] radioStations = [];
        
        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            // Start with base query and apply performance optimizations
            var query = scopedContext.RadioStations.AsNoTracking();
            
            // Apply filters if specified
            query = ApplyFilters(query, pagedRequest);
            
            // Get total count with filters applied
            radioStationCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
            
            if (!pagedRequest.IsTotalCountOnlyRequest)
            {
                // Apply ordering
                query = ApplyOrdering(query, pagedRequest);
                
                // Apply pagination
                radioStations = await query
                    .Skip(pagedRequest.SkipValue)
                    .Take(pagedRequest.TakeValue)
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return new MelodeeModels.PagedResult<RadioStation>
        {
            TotalCount = radioStationCount,
            TotalPages = pagedRequest.TotalPages(radioStationCount),
            Data = radioStations
        };
    }

    public async Task<MelodeeModels.OperationResult<RadioStation?>> AddAsync(RadioStation radioStation,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(radioStation, nameof(radioStation));

        radioStation.ApiKey = Guid.NewGuid();

        radioStation.CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);

        var validationResult = ValidateModel(radioStation);
        if (!validationResult.IsSuccess)
        {
            return new MelodeeModels.OperationResult<RadioStation?>(validationResult.Data.Item2
                ?.Where(x => !string.IsNullOrWhiteSpace(x.ErrorMessage)).Select(x => x.ErrorMessage!).ToArray() ?? [])
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.ValidationFailure
            };
        }

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            scopedContext.RadioStations.Add(radioStation);
            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetAsync(radioStation.Id, cancellationToken);
    }

    public async Task<MelodeeModels.OperationResult<RadioStation?>> GetAsync(int id,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, id, nameof(id));

        var result = await CacheManager.GetAsync(CacheKeyDetailTemplate.FormatSmart(id), async () =>
        {
            await using (var scopedContext =
                         await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await scopedContext
                    .RadioStations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                    .ConfigureAwait(false);
            }
        }, cancellationToken);
        return new MelodeeModels.OperationResult<RadioStation?>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> DeleteAsync(int currentUserId, int[] radioStationIds,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(radioStationIds, nameof(radioStationIds));

        bool result;

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var user = await scopedContext.Users.FirstAsync(x => x.Id == currentUserId, cancellationToken)
                .ConfigureAwait(false);

            if (!user.IsAdmin)
            {
                return new MelodeeModels.OperationResult<bool>("Non admin users cannot delete RadioStations.")
                {
                    Type = MelodeeModels.OperationResponseType.AccessDenied,
                    Data = false
                };
            }


            foreach (var radioStationId in radioStationIds)
            {
                var radioStation = await GetAsync(radioStationId, cancellationToken).ConfigureAwait(false);
                if (!radioStation.IsSuccess)
                {
                    return new MelodeeModels.OperationResult<bool>("Unknown RadioStation.")
                    {
                        Data = false
                    };
                }
            }

            foreach (var radioStationId in radioStationIds)
            {
                var radioStation = await scopedContext
                    .RadioStations
                    .FirstAsync(x => x.Id == radioStationId, cancellationToken)
                    .ConfigureAwait(false);
                scopedContext.RadioStations.Remove(radioStation);
            }

            result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    private static IQueryable<RadioStation> ApplyFilters(IQueryable<RadioStation> query, MelodeeModels.PagedRequest pagedRequest)
    {
        if (pagedRequest.FilterBy == null || pagedRequest.FilterBy.Length == 0)
        {
            return query;
        }

        // Single filter - direct application for performance
        if (pagedRequest.FilterBy.Length == 1)
        {
            var filter = pagedRequest.FilterBy[0];
            var filterValue = filter.Value?.ToString()?.ToLowerInvariant() ?? string.Empty;
            
            return filter.PropertyName.ToLowerInvariant() switch
            {
                "name" => filter.Operator switch
                {
                    FilterOperator.Contains => query.Where(r => r.Name.ToLower().Contains(filterValue)),
                    FilterOperator.Equals => query.Where(r => r.Name.ToLower() == filterValue),
                    FilterOperator.StartsWith => query.Where(r => r.Name.ToLower().StartsWith(filterValue)),
                    FilterOperator.EndsWith => query.Where(r => r.Name.ToLower().EndsWith(filterValue)),
                    FilterOperator.NotEquals => query.Where(r => r.Name.ToLower() != filterValue),
                    _ => query
                },
                "streamurl" => filter.Operator switch
                {
                    FilterOperator.Contains => query.Where(r => r.StreamUrl.ToLower().Contains(filterValue)),
                    FilterOperator.Equals => query.Where(r => r.StreamUrl.ToLower() == filterValue),
                    FilterOperator.StartsWith => query.Where(r => r.StreamUrl.ToLower().StartsWith(filterValue)),
                    FilterOperator.EndsWith => query.Where(r => r.StreamUrl.ToLower().EndsWith(filterValue)),
                    FilterOperator.NotEquals => query.Where(r => r.StreamUrl.ToLower() != filterValue),
                    _ => query
                },
                "homepageurl" => filter.Operator switch
                {
                    FilterOperator.Contains => query.Where(r => r.HomePageUrl != null && r.HomePageUrl.ToLower().Contains(filterValue)),
                    FilterOperator.Equals => query.Where(r => r.HomePageUrl != null && r.HomePageUrl.ToLower() == filterValue),
                    FilterOperator.StartsWith => query.Where(r => r.HomePageUrl != null && r.HomePageUrl.ToLower().StartsWith(filterValue)),
                    FilterOperator.EndsWith => query.Where(r => r.HomePageUrl != null && r.HomePageUrl.ToLower().EndsWith(filterValue)),
                    FilterOperator.NotEquals => query.Where(r => r.HomePageUrl == null || r.HomePageUrl.ToLower() != filterValue),
                    _ => query
                },
                "description" => filter.Operator switch
                {
                    FilterOperator.Contains => query.Where(r => r.Description != null && r.Description.ToLower().Contains(filterValue)),
                    FilterOperator.Equals => query.Where(r => r.Description != null && r.Description.ToLower() == filterValue),
                    FilterOperator.StartsWith => query.Where(r => r.Description != null && r.Description.ToLower().StartsWith(filterValue)),
                    FilterOperator.EndsWith => query.Where(r => r.Description != null && r.Description.ToLower().EndsWith(filterValue)),
                    FilterOperator.NotEquals => query.Where(r => r.Description == null || r.Description.ToLower() != filterValue),
                    _ => query
                },
                "tags" => filter.Operator switch
                {
                    FilterOperator.Contains => query.Where(r => r.Tags != null && r.Tags.ToLower().Contains(filterValue)),
                    FilterOperator.Equals => query.Where(r => r.Tags != null && r.Tags.ToLower() == filterValue),
                    FilterOperator.StartsWith => query.Where(r => r.Tags != null && r.Tags.ToLower().StartsWith(filterValue)),
                    FilterOperator.EndsWith => query.Where(r => r.Tags != null && r.Tags.ToLower().EndsWith(filterValue)),
                    FilterOperator.NotEquals => query.Where(r => r.Tags == null || r.Tags.ToLower() != filterValue),
                    _ => query
                },
                "islocked" => filter.Operator switch
                {
                    FilterOperator.Equals => bool.TryParse(filterValue, out var boolValue) ? query.Where(r => r.IsLocked == boolValue) : query,
                    FilterOperator.NotEquals => bool.TryParse(filterValue, out var boolValue2) ? query.Where(r => r.IsLocked != boolValue2) : query,
                    _ => query
                },
                _ => query
            };
        }

        // Multiple filters - build expression tree for complex OR/AND logic
        var filterPredicates = new List<Expression<Func<RadioStation, bool>>>();
        
        foreach (var filter in pagedRequest.FilterBy)
        {
            var filterValue = filter.Value?.ToString()?.ToLowerInvariant() ?? string.Empty;
            Expression<Func<RadioStation, bool>>? predicate = filter.PropertyName.ToLowerInvariant() switch
            {
                "name" => filter.Operator switch
                {
                    FilterOperator.Contains => r => r.Name.ToLower().Contains(filterValue),
                    FilterOperator.Equals => r => r.Name.ToLower() == filterValue,
                    FilterOperator.StartsWith => r => r.Name.ToLower().StartsWith(filterValue),
                    FilterOperator.EndsWith => r => r.Name.ToLower().EndsWith(filterValue),
                    FilterOperator.NotEquals => r => r.Name.ToLower() != filterValue,
                    _ => null
                },
                "streamurl" => filter.Operator switch
                {
                    FilterOperator.Contains => r => r.StreamUrl.ToLower().Contains(filterValue),
                    FilterOperator.Equals => r => r.StreamUrl.ToLower() == filterValue,
                    FilterOperator.StartsWith => r => r.StreamUrl.ToLower().StartsWith(filterValue),
                    FilterOperator.EndsWith => r => r.StreamUrl.ToLower().EndsWith(filterValue),
                    FilterOperator.NotEquals => r => r.StreamUrl.ToLower() != filterValue,
                    _ => null
                },
                _ => null
            };
            
            if (predicate != null)
            {
                filterPredicates.Add(predicate);
            }
        }

        // Combine predicates with OR logic (following ArtistService pattern)
        if (filterPredicates.Count > 0)
        {
            var combinedPredicate = filterPredicates[0];
            for (int i = 1; i < filterPredicates.Count; i++)
            {
                combinedPredicate = CombinePredicates(combinedPredicate, filterPredicates[i], ExpressionType.OrElse);
            }
            query = query.Where(combinedPredicate);
        }

        return query;
    }

    private static Expression<Func<RadioStation, bool>> CombinePredicates(
        Expression<Func<RadioStation, bool>> left,
        Expression<Func<RadioStation, bool>> right,
        ExpressionType operation)
    {
        var parameter = Expression.Parameter(typeof(RadioStation), "r");
        var leftInvoke = Expression.Invoke(left, parameter);
        var rightInvoke = Expression.Invoke(right, parameter);
        var combined = Expression.MakeBinary(operation, leftInvoke, rightInvoke);
        return Expression.Lambda<Func<RadioStation, bool>>(combined, parameter);
    }

    private static IQueryable<RadioStation> ApplyOrdering(IQueryable<RadioStation> query, MelodeeModels.PagedRequest pagedRequest)
    {
        var orderByClause = pagedRequest.OrderByValue("Name", MelodeeModels.PagedRequest.OrderAscDirection);
        var isDescending = orderByClause.Contains("DESC", StringComparison.OrdinalIgnoreCase);
        var fieldName = orderByClause.Split(' ')[0].Trim('"').ToLowerInvariant();

        return fieldName switch
        {
            "name" => isDescending ? query.OrderByDescending(r => r.Name) : query.OrderBy(r => r.Name),
            "streamurl" => isDescending ? query.OrderByDescending(r => r.StreamUrl) : query.OrderBy(r => r.StreamUrl),
            "homepageurl" => isDescending ? query.OrderByDescending(r => r.HomePageUrl) : query.OrderBy(r => r.HomePageUrl),
            "createdat" => isDescending ? query.OrderByDescending(r => r.CreatedAt) : query.OrderBy(r => r.CreatedAt),
            "lastupdatedat" => isDescending ? query.OrderByDescending(r => r.LastUpdatedAt) : query.OrderBy(r => r.LastUpdatedAt),
            "sortorder" => isDescending ? query.OrderByDescending(r => r.SortOrder) : query.OrderBy(r => r.SortOrder),
            "islocked" => isDescending ? query.OrderByDescending(r => r.IsLocked) : query.OrderBy(r => r.IsLocked),
            "id" or _ => isDescending ? query.OrderByDescending(r => r.Id) : query.OrderBy(r => r.Id)
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> UpdateAsync(RadioStation detailToUpdate,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, detailToUpdate.Id, nameof(detailToUpdate));

        bool result;
        var validationResult = ValidateModel(detailToUpdate);
        if (!validationResult.IsSuccess)
        {
            return new MelodeeModels.OperationResult<bool>(validationResult.Data.Item2
                ?.Where(x => !string.IsNullOrWhiteSpace(x.ErrorMessage)).Select(x => x.ErrorMessage!).ToArray() ?? [])
            {
                Data = false,
                Type = MelodeeModels.OperationResponseType.ValidationFailure
            };
        }

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            // Load the detail by detailToUpdate.Id
            var dbDetail = await scopedContext
                .RadioStations
                .FirstOrDefaultAsync(x => x.Id == detailToUpdate.Id, cancellationToken)
                .ConfigureAwait(false);

            if (dbDetail == null)
            {
                return new MelodeeModels.OperationResult<bool>
                {
                    Data = false,
                    Type = MelodeeModels.OperationResponseType.NotFound
                };
            }

            // Update values and save to db
            dbDetail.Name = detailToUpdate.Name;
            dbDetail.StreamUrl = detailToUpdate.StreamUrl;
            dbDetail.HomePageUrl = detailToUpdate.HomePageUrl;
            dbDetail.Description = detailToUpdate.Description;
            dbDetail.IsLocked = detailToUpdate.IsLocked;
            dbDetail.Notes = detailToUpdate.Notes;
            dbDetail.SortOrder = detailToUpdate.SortOrder;
            dbDetail.Tags = detailToUpdate.Tags;
            dbDetail.LastUpdatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);

            result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;

            if (result)
            {
                CacheManager.Remove(CacheKeyDetailTemplate.FormatSmart(dbDetail.Id));
                CacheManager.Remove(CacheKeyDetailByApiKeyTemplate.FormatSmart(dbDetail.ApiKey));
            }
        }


        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }
}
