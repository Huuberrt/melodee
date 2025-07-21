using Ardalis.GuardClauses;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Extensions;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;
using SmartFormat;
using Sqids;
using System.Linq.Expressions;
using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Common.Services;

public class ShareService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    private const string CacheKeyDetailByApiKeyTemplate = "urn:share:apikey:{0}";
    private const string CacheKeyDetailTemplate = "urn:share:{0}";

    public async Task<MelodeeModels.PagedResult<Share>> ListAsync(MelodeeModels.PagedRequest pagedRequest,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        
        // Create base query with optimized includes
        var baseQuery = scopedContext.Shares
            .Include(s => s.User)
            .AsNoTracking();
        
        // Apply filters using EF Core
        var filteredQuery = ApplyFilters(baseQuery, pagedRequest);
        
        // Get total count efficiently
        var shareCount = await filteredQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        
        Share[] shares = [];
        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            // Apply ordering and paging
            var orderedQuery = ApplyOrdering(filteredQuery, pagedRequest);
            shares = await orderedQuery
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return new MelodeeModels.PagedResult<Share>
        {
            TotalCount = shareCount,
            TotalPages = pagedRequest.TotalPages(shareCount),
            Data = shares
        };
    }

    public async Task<MelodeeModels.OperationResult<Share?>> AddAsync(Share share,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(share, nameof(share));

        share.ApiKey = Guid.NewGuid();

        var sqids = new SqidsEncoder<long>();
        share.ShareUniqueId = sqids.Encode(DateTime.UtcNow.Ticks);

        share.CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);

        var validationResult = ValidateModel(share);
        if (!validationResult.IsSuccess)
        {
            return new MelodeeModels.OperationResult<Share?>(validationResult.Data.Item2
                ?.Where(x => !string.IsNullOrWhiteSpace(x.ErrorMessage)).Select(x => x.ErrorMessage!).ToArray() ?? [])
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.ValidationFailure
            };
        }

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            scopedContext.Shares.Add(share);
            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetAsync(share.Id, cancellationToken);
    }

    public async Task<MelodeeModels.OperationResult<Share?>> GetByUniqueIdAsync(string id,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(id, nameof(id));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        
        // Use EF Core to find the share by unique ID with optimized query
        var share = await scopedContext.Shares
            .Include(s => s.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ShareUniqueId == id, cancellationToken)
            .ConfigureAwait(false);

        return new MelodeeModels.OperationResult<Share?>
        {
            Data = share
        };
    }

    public async Task<MelodeeModels.OperationResult<Share?>> GetByApiKeyAsync(Guid apiKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Default(apiKey, nameof(apiKey));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        
        var share = await scopedContext.Shares
            .Include(s => s.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ApiKey == apiKey, cancellationToken)
            .ConfigureAwait(false);

        return new MelodeeModels.OperationResult<Share?>
        {
            Data = share
        };
    }

    public async Task<MelodeeModels.OperationResult<Share?>> GetAsync(int id,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, id, nameof(id));

        var result = await CacheManager.GetAsync(CacheKeyDetailTemplate.FormatSmart(id), async () =>
        {
            await using (var scopedContext =
                         await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await scopedContext
                    .Shares
                    .Include(x => x.User)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                    .ConfigureAwait(false);
            }
        }, cancellationToken);
        return new MelodeeModels.OperationResult<Share?>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> DeleteAsync(int currentUserId, int[] shareIds,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(shareIds, nameof(shareIds));

        bool result;

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var user = await scopedContext.Users.FirstAsync(x => x.Id == currentUserId, cancellationToken)
                .ConfigureAwait(false);
            foreach (var shareId in shareIds)
            {
                var share = await GetAsync(shareId, cancellationToken).ConfigureAwait(false);
                if (!share.IsSuccess)
                {
                    return new MelodeeModels.OperationResult<bool>("Unknown share.")
                    {
                        Data = false
                    };
                }
            }

            foreach (var shareId in shareIds)
            {
                var share = await scopedContext
                    .Shares
                    .FirstAsync(x => x.Id == shareId, cancellationToken)
                    .ConfigureAwait(false);

                if (share.UserId != currentUserId && !user.IsAdmin)
                {
                    return new MelodeeModels.OperationResult<bool>("Non admin users cannot delete other users shares.")
                    {
                        Type = MelodeeModels.OperationResponseType.AccessDenied,
                        Data = false
                    };
                }

                scopedContext.Shares.Remove(share);
            }

            result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> UpdateAsync(Share detailToUpdate,
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
            // Load the detail by DetailToUpdate.Id
            var dbDetail = await scopedContext
                .Shares
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
            dbDetail.Description = detailToUpdate.Description;
            dbDetail.IsLocked = detailToUpdate.IsLocked;
            dbDetail.Notes = detailToUpdate.Notes;
            dbDetail.SortOrder = detailToUpdate.SortOrder;
            dbDetail.Tags = detailToUpdate.Tags;
            dbDetail.ShareType = detailToUpdate.ShareType;
            dbDetail.ShareId = detailToUpdate.ShareId;


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

    /// <summary>
    ///     Apply filters to the Share query based on PagedRequest FilterBy criteria
    /// </summary>
    private static IQueryable<Share> ApplyFilters(IQueryable<Share> query, MelodeeModels.PagedRequest pagedRequest)
    {
        if (pagedRequest.FilterBy == null || pagedRequest.FilterBy.Length == 0)
        {
            return query;
        }

        foreach (var filter in pagedRequest.FilterBy)
        {
            var value = filter.Value.ToString();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            var normalizedValue = value.ToNormalizedString() ?? value;
            
            query = filter.PropertyName.ToLowerInvariant() switch
            {
                "userid" => int.TryParse(value, out var userIdValue)
                    ? query.Where(s => s.UserId == userIdValue)
                    : query,
                "shareid" => int.TryParse(value, out var shareIdValue) 
                    ? query.Where(s => s.ShareId == shareIdValue)
                    : query,
                "sharetype" => int.TryParse(value, out var shareTypeValue)
                    ? query.Where(s => s.ShareType == shareTypeValue)
                    : query,
                "shareuniqueid" => query.Where(s => s.ShareUniqueId.Contains(normalizedValue)),
                "description" => query.Where(s => s.Description != null && s.Description.Contains(normalizedValue)),
                "notes" => query.Where(s => s.Notes != null && s.Notes.Contains(normalizedValue)),
                "tags" => query.Where(s => s.Tags != null && s.Tags.Contains(normalizedValue)),
                "islocked" => bool.TryParse(value, out var isLockedValue)
                    ? query.Where(s => s.IsLocked == isLockedValue)
                    : query,
                "isdownloadable" => bool.TryParse(value, out var isDownloadableValue)
                    ? query.Where(s => s.IsDownloadable == isDownloadableValue)
                    : query,
                "username" => query.Where(s => s.User.UserName.Contains(normalizedValue)),
                "useremail" => query.Where(s => s.User.Email.Contains(normalizedValue)),
                _ => query
            };
        }

        return query;
    }

    /// <summary>
    ///     Apply ordering to the Share query based on PagedRequest OrderBy criteria
    /// </summary>
    private static IQueryable<Share> ApplyOrdering(IQueryable<Share> query, MelodeeModels.PagedRequest pagedRequest)
    {
        if (pagedRequest.OrderBy == null || pagedRequest.OrderBy.Count == 0)
        {
            // Default ordering by Id ascending
            return query.OrderBy(s => s.Id);
        }

        IOrderedQueryable<Share>? orderedQuery = null;
        var isFirst = true;

        foreach (var orderBy in pagedRequest.OrderBy)
        {
            var isDescending = orderBy.Value.Equals("DESC", StringComparison.OrdinalIgnoreCase);
            
            Expression<Func<Share, object>> keySelector = orderBy.Key.ToLowerInvariant() switch
            {
                "id" => s => s.Id,
                "userid" => s => s.UserId,
                "shareid" => s => s.ShareId,
                "sharetype" => s => s.ShareType,
                "shareuniqueid" => s => s.ShareUniqueId,
                "description" => s => s.Description ?? string.Empty,
                "notes" => s => s.Notes ?? string.Empty,
                "tags" => s => s.Tags ?? string.Empty,
                "islocked" => s => s.IsLocked,
                "isdownloadable" => s => s.IsDownloadable,
                "sortorder" => s => s.SortOrder,
                "visitcount" => s => s.VisitCount,
                "createdat" => s => s.CreatedAt,
                "lastupdatedat" => s => s.LastUpdatedAt ?? Instant.MinValue,
                "expiresat" => s => s.ExpiresAt ?? Instant.MinValue,
                "lastvisitedat" => s => s.LastVisitedAt ?? Instant.MinValue,
                "username" => s => s.User.UserName,
                "useremail" => s => s.User.Email,
                _ => s => s.Id // fallback to Id
            };

            if (isFirst)
            {
                orderedQuery = isDescending 
                    ? query.OrderByDescending(keySelector)
                    : query.OrderBy(keySelector);
                isFirst = false;
            }
            else
            {
                orderedQuery = isDescending
                    ? orderedQuery!.ThenByDescending(keySelector)
                    : orderedQuery!.ThenBy(keySelector);
            }
        }

        return orderedQuery ?? query.OrderBy(s => s.Id);
    }
}
