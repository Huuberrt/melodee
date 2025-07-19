using Ardalis.GuardClauses;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Extensions;
using Melodee.Common.Filtering;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;
using SmartFormat;
using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Common.Services;

/// <summary>
///     Setting data domain service, this is used to manage the settings, for getting settings for services see
///     <see cref="IMelodeeConfigurationFactory" />
/// </summary>
public class SettingService : ServiceBase
{
    private const string CacheKeyDetailTemplate = "urn:setting:{0}";
    private readonly IMelodeeConfigurationFactory _melodeeConfigurationFactory = null!;

    /// <summary>
    ///     This is required for Mocking in unit tests.
    /// </summary>
    public SettingService()
    {
    }

    /// <summary>
    ///     Setting data domain service, this is used to manage the settings, for getting settings for services see
    ///     <see cref="IMelodeeConfigurationFactory" />
    /// </summary>
    public SettingService(ILogger logger,
        ICacheManager cacheManager,
        IMelodeeConfigurationFactory melodeeConfigurationFactory,
        IDbContextFactory<MelodeeDbContext> contextFactory) : base(logger, cacheManager, contextFactory)
    {
        _melodeeConfigurationFactory = melodeeConfigurationFactory;
    }

    public virtual async Task<Dictionary<string, object?>> GetAllSettingsAsync(CancellationToken cancellationToken = default)
    {
        var listResult = await ListAsync(new MelodeeModels.PagedRequest { PageSize = short.MaxValue }, cancellationToken);
        if (!listResult.IsSuccess)
        {
            throw new Exception("Failed to get settings from database");
        }

        var listDictionary = listResult.Data.ToDictionary(x => x.Key, x => (object?)x.Value);
        return MelodeeConfiguration.AllSettings(listDictionary);
    }

    public async Task<MelodeeModels.PagedResult<Setting>> ListAsync(MelodeeModels.PagedRequest pagedRequest, CancellationToken cancellationToken = default)
    {
        var settingsCount = 0;
        Setting[] settings = [];
        
        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                // Build base query with AsNoTracking for performance
                var query = scopedContext.Settings.AsNoTracking().AsQueryable();
                
                // Apply dynamic filtering using EF Core
                query = ApplyFilters(query, pagedRequest);
                
                // Get total count efficiently
                settingsCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
                
                if (!pagedRequest.IsTotalCountOnlyRequest)
                {
                    // Apply ordering, pagination and execute query
                    query = ApplyOrdering(query, pagedRequest);
                    query = query.Skip(pagedRequest.SkipValue).Take(pagedRequest.TakeValue);
                    
                    settings = await query.ToArrayAsync(cancellationToken).ConfigureAwait(false);

                    // Apply environment variable overrides
                    ApplyEnvironmentVariableOverrides(settings);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to get settings from database");
            }
        }

        return new MelodeeModels.PagedResult<Setting>
        {
            TotalCount = settingsCount,
            TotalPages = pagedRequest.TotalPages(settingsCount),
            Data = settings
        };
    }

    /// <summary>
    /// Applies dynamic filtering to the query based on PagedRequest filters using EF Core LINQ
    /// </summary>
    private static IQueryable<Setting> ApplyFilters(IQueryable<Setting> query, MelodeeModels.PagedRequest pagedRequest)
    {
        if (pagedRequest.FilterBy == null || pagedRequest.FilterBy.Length == 0)
        {
            return query;
        }

        foreach (var filter in pagedRequest.FilterBy)
        {
            query = filter.Operator switch
            {
                FilterOperator.Equals => query.Where(s => EF.Property<string>(s, filter.PropertyName) == filter.Value.ToString()),
                FilterOperator.NotEquals => query.Where(s => EF.Property<string>(s, filter.PropertyName) != filter.Value.ToString()),
                FilterOperator.Contains => query.Where(s => EF.Property<string>(s, filter.PropertyName).Contains(filter.Value.ToString()!)),
                FilterOperator.DoesNotContain => query.Where(s => !EF.Property<string>(s, filter.PropertyName).Contains(filter.Value.ToString()!)),
                FilterOperator.StartsWith => query.Where(s => EF.Property<string>(s, filter.PropertyName).StartsWith(filter.Value.ToString()!)),
                FilterOperator.EndsWith => query.Where(s => EF.Property<string>(s, filter.PropertyName).EndsWith(filter.Value.ToString()!)),
                FilterOperator.IsNull => query.Where(s => EF.Property<string>(s, filter.PropertyName) == null),
                FilterOperator.IsNotNull => query.Where(s => EF.Property<string>(s, filter.PropertyName) != null),
                FilterOperator.IsEmpty => query.Where(s => EF.Property<string>(s, filter.PropertyName) == string.Empty),
                FilterOperator.IsNotEmpty => query.Where(s => EF.Property<string>(s, filter.PropertyName) != string.Empty),
                FilterOperator.GreaterThan when filter.Value.IsNumericType() => 
                    query.Where(s => EF.Property<int>(s, filter.PropertyName) > Convert.ToInt32(filter.Value)),
                FilterOperator.GreaterThanOrEquals when filter.Value.IsNumericType() => 
                    query.Where(s => EF.Property<int>(s, filter.PropertyName) >= Convert.ToInt32(filter.Value)),
                FilterOperator.LessThan when filter.Value.IsNumericType() => 
                    query.Where(s => EF.Property<int>(s, filter.PropertyName) < Convert.ToInt32(filter.Value)),
                FilterOperator.LessThanOrEquals when filter.Value.IsNumericType() => 
                    query.Where(s => EF.Property<int>(s, filter.PropertyName) <= Convert.ToInt32(filter.Value)),
                _ => query
            };
        }

        return query;
    }

    /// <summary>
    /// Applies dynamic ordering to the query based on PagedRequest OrderBy using EF Core
    /// </summary>
    private static IQueryable<Setting> ApplyOrdering(IQueryable<Setting> query, MelodeeModels.PagedRequest pagedRequest)
    {
        var orderBy = pagedRequest.OrderBy;
        if (orderBy == null || orderBy.Count == 0)
        {
            // Default ordering by Id ASC
            return query.OrderBy(s => s.Id);
        }

        IOrderedQueryable<Setting>? orderedQuery = null;
        var isFirst = true;

        foreach (var orderPair in orderBy)
        {
            var propertyName = orderPair.Key;
            var direction = orderPair.Value;
            var isAscending = string.Equals(direction, "ASC", StringComparison.OrdinalIgnoreCase);

            if (isFirst)
            {
                orderedQuery = isAscending
                    ? query.OrderBy(s => EF.Property<object>(s, propertyName))
                    : query.OrderByDescending(s => EF.Property<object>(s, propertyName));
                isFirst = false;
            }
            else
            {
                orderedQuery = isAscending
                    ? orderedQuery!.ThenBy(s => EF.Property<object>(s, propertyName))
                    : orderedQuery!.ThenByDescending(s => EF.Property<object>(s, propertyName));
            }
        }

        return orderedQuery ?? query.OrderBy(s => s.Id);
    }

    /// <summary>
    /// Applies environment variable overrides to settings in memory
    /// </summary>
    private static void ApplyEnvironmentVariableOverrides(Setting[] settings)
    {
        foreach (var envSetSetting in MelodeeConfigurationFactory.EnvironmentVariablesSettings())
        {
            var normalizedKey = envSetSetting.Key.Replace("_", ".");
            var setting = settings.FirstOrDefault(x => string.Equals(x.Key, normalizedKey, StringComparison.OrdinalIgnoreCase));
            if (setting != null)
            {
                setting.Value = envSetSetting.Value?.ToString() ?? string.Empty;
            }
        }
    }

    public async Task<MelodeeModels.OperationResult<T?>> GetValueAsync<T>(string key, T? defaultValue = default,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(key, nameof(key));

        var settingResult = await GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (settingResult.Data == null || !settingResult.IsSuccess)
        {
            return new MelodeeModels.OperationResult<T?>
            {
                Data = defaultValue ?? default,
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        return new MelodeeModels.OperationResult<T?>
        {
            Data = settingResult.Data.Value.Convert<T>()
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> SetAsync(string key, string value,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(key, nameof(key));

        var setting = await GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (setting.Data != null)
        {
            setting.Data.Value = value;
            return await UpdateAsync(setting.Data, cancellationToken).ConfigureAwait(false);
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = false
        };
    }

    public async Task<MelodeeModels.OperationResult<Setting?>> GetAsync(string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await CacheManager.GetAsync(CacheKeyDetailTemplate.FormatSmart(key), async () =>
            {
                await using (var scopedContext =
                             await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
                {
                    return await scopedContext
                        .Settings
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Key == key, cancellationToken)
                        .ConfigureAwait(false);
                }
            }, cancellationToken);
            return new MelodeeModels.OperationResult<Setting?>
            {
                Data = result
            };
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to get setting [{0}]", key);
        }

        return new MelodeeModels.OperationResult<Setting?>
        {
            Data = default,
            Type = MelodeeModels.OperationResponseType.Error
        };
    }

    public async Task<MelodeeModels.OperationResult<Setting?>> AddAsync(Setting setting, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(setting, nameof(setting));

        setting.ApiKey = Guid.NewGuid();
        setting.CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);

        var validationResult = ValidateModel(setting);
        if (!validationResult.IsSuccess)
        {
            return new MelodeeModels.OperationResult<Setting?>(validationResult.Data.Item2
                ?.Where(x => !string.IsNullOrWhiteSpace(x.ErrorMessage)).Select(x => x.ErrorMessage!).ToArray() ?? [])
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.ValidationFailure
            };
        }

        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            // Ensure the setting key is unique
            var existingSetting = await scopedContext
                .Settings
                .FirstOrDefaultAsync(x => x.Key == setting.Key, cancellationToken)
                .ConfigureAwait(false);
            if (existingSetting != null)
            {
                return new MelodeeModels.OperationResult<Setting?>([$"Setting with key '{setting.Key}' already exists."])
                {
                    Data = null,
                    Type = MelodeeModels.OperationResponseType.Error
                };
            }

            scopedContext.Settings.Add(setting);
            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetAsync(setting.Key, cancellationToken);
    }

    public async Task<MelodeeModels.OperationResult<bool>> UpdateAsync(Setting detailToUpdate,
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
                .Settings
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
            dbDetail.Category = detailToUpdate.Category;
            dbDetail.Comment = detailToUpdate.Comment;
            dbDetail.Description = detailToUpdate.Description;
            dbDetail.IsLocked = detailToUpdate.IsLocked;
            dbDetail.Key = detailToUpdate.Key;
            dbDetail.Notes = detailToUpdate.Notes;
            dbDetail.SortOrder = detailToUpdate.SortOrder;
            dbDetail.Tags = detailToUpdate.Tags;
            dbDetail.Value = detailToUpdate.Value;

            dbDetail.LastUpdatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);

            result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;

            if (result)
            {
                CacheManager.Remove(CacheKeyDetailTemplate.FormatSmart(dbDetail.Id));
                _melodeeConfigurationFactory.Reset();
            }
        }


        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }
}
