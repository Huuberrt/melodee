using System.Diagnostics;
using System.IO.Compression;
using Asp.Versioning;
using Microsoft.AspNetCore.HttpOverrides;
using Blazored.SessionStorage;
using Melodee.Blazor.Components;
using Melodee.Blazor.Constants;
using Melodee.Blazor.Filters;
using Melodee.Blazor.Middleware;
using Melodee.Blazor.Services;
using Melodee.Common.Configuration;
using Microsoft.AspNetCore.ResponseCompression;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Jobs;
using Melodee.Common.MessageBus.EventHandlers;
using Melodee.Common.Metadata;
using Melodee.Common.Models;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Melodee.Common.Plugins.MetaData.Song;
using Melodee.Common.Plugins.Scrobbling;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Common.Plugins.SearchEngine.Spotify;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Scanning;
using Melodee.Common.Services.SearchEngines;
using Melodee.Common.Utility;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Quartz.AspNetCore;
using Radzen;
using Rebus.Compression;
using Rebus.Config;
using Rebus.Persistence.InMem;
using Rebus.Transport.InMem;
using Serilog;
using SpotifyAPI.Web;
using ILogger = Serilog.ILogger;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", false, true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true)
    .AddEnvironmentVariables();

Trace.Listeners.Clear();
Trace.Listeners.Add(new ConsoleTraceListener());

builder.Host.UseSerilog((hostingContext, loggerConfiguration)
    => loggerConfiguration.ReadFrom.Configuration(hostingContext.Configuration));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers(options => { options.Filters.Add<ETagFilter>(); });

// Build connection string with optional pool-size overrides via environment variables
var defaultConnString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(defaultConnString))
{
    throw new InvalidOperationException("Missing connection string 'DefaultConnection'");
}

var npgsqlBuilder = new NpgsqlConnectionStringBuilder(defaultConnString);
var envMinPool = Environment.GetEnvironmentVariable("DB_MIN_POOL_SIZE");
var envMaxPool = Environment.GetEnvironmentVariable("DB_MAX_POOL_SIZE");
if (int.TryParse(envMinPool, out var minPool) && minPool > 0)
{
    npgsqlBuilder.MinPoolSize = minPool;
}
if (int.TryParse(envMaxPool, out var maxPool) && maxPool > 0)
{
    npgsqlBuilder.MaxPoolSize = maxPool;
}
var effectiveConnString = npgsqlBuilder.ToString();

builder.Services.AddDbContextFactory<MelodeeDbContext>(opt =>
    opt.UseNpgsql(effectiveConnString, o
        => o.UseNodaTime()
            .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));

builder.Services.AddDbContextFactory<ArtistSearchEngineServiceDbContext>(opt
    => opt.UseSqlite(builder.Configuration.GetConnectionString("ArtistSearchEngineConnection")));

builder.Services.AddDbContextFactory<MusicBrainzDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("MusicBrainzConnection")));

builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1);
        options.ReportApiVersions = true;
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ApiVersionReader = ApiVersionReader.Combine(
            new UrlSegmentApiVersionReader(),
            new HeaderApiVersionReader("X-Api-Version"));
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'V";
        options.SubstituteApiVersionInUrl = true;
    });


// Configure forwarded headers for reverse proxy (only if enabled)
var useForwardedHeaders = SafeParser.ToBoolean(builder.Configuration["UseForwardedHeaders"]);
if (useForwardedHeaders)
{
    Trace.WriteLine("Using forwarded headers");
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

builder.Services.AddAntiforgery(opt =>
{
    opt.Cookie.Name = "melodee_csrf";
    opt.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddRadzenComponents();

builder.Services.AddRadzenCookieThemeService(options =>
{
    options.Name = "melodee_ui_theme";
    options.Duration = TimeSpan.FromDays(9999);
});

builder.Services.AddBlazoredSessionStorage();

// Add response compression for better performance (addresses Lighthouse: Enable text compression)
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/javascript",
        "application/json",
        "text/css",
        "text/html",
        "text/json",
        "text/plain"
    });
});

builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Optimal;
});

builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Optimal;
});

// Configure HSTS options (addresses Lighthouse: HSTS security)
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;
});

builder.Services.AddAuthorization();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(x =>
    {
        x.Cookie.SameSite = SameSiteMode.Strict;
        x.Cookie.Name = "melodee_auth";
    });
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<ILocalStorageService, LocalStorageService>();

// Health checks
builder.Services.AddHealthChecks();

#region Melodee Services

builder.Services
    .AddScoped<MainLayoutProxyService>()
    .AddSingleton<ISerializer, Serializer>()
    .AddSingleton<ICacheManager>(opt
        => new MemoryCacheManager(opt.GetRequiredService<ILogger>(),
            new TimeSpan(1,
                0,
                0,
                0),
            opt.GetRequiredService<ISerializer>()))
    .AddSingleton<DefaultImages>(_ => new DefaultImages
    {
        UserAvatarBytes = File.ReadAllBytes("wwwroot/images/avatar.png"),
        AlbumCoverBytes = File.ReadAllBytes("wwwroot/images/album.jpg"),
        ArtistBytes = File.ReadAllBytes("wwwroot/images/artist.jpg"),
        PlaylistImageBytes = File.ReadAllBytes("wwwroot/images/playlist.jpg")
    })
    .AddSingleton(SpotifyClientConfig.CreateDefault())
    .AddScoped<ISpotifyClientBuilder, SpotifyClientBuilder>()
    .AddSingleton<IFileSystemService, FileSystemService>()
    .AddSingleton<INowPlayingRepository, NowPlayingInMemoryRepository>()
    .AddSingleton<IMelodeeConfigurationFactory, MelodeeConfigurationFactory>()
    .AddSingleton<StreamingLimiter>()
    .AddSingleton<EtagRepository>()
    .AddScoped<IMusicBrainzRepository, SQLiteMusicBrainzRepository>()
    .AddScoped<SettingService>()
    .AddScoped<ArtistService>()
    .AddScoped<IBaseUrlService, BaseUrlService>()
    .AddScoped<AlbumService>()
    .AddScoped<SongService>()
    .AddScoped<ScrobbleService>()
    .AddScoped<LibraryService>()
    .AddScoped<UserService>()
    .AddScoped<AlbumDiscoveryService>()
    .AddScoped<MediaEditService>()
    .AddScoped<DirectoryProcessorToStagingService>()
    .AddScoped<ImageConversionService>()
    .AddScoped<OpenSubsonicApiService>()
    .AddScoped<AlbumImageSearchEngineService>()
    .AddScoped<ArtistImageSearchEngineService>()
    .AddScoped<AlbumSearchEngineService>()
    .AddScoped<ArtistSearchEngineService>()
    .AddScoped<StatisticsService>()
    .AddScoped<SearchService>()
    .AddScoped<ShareService>()
    .AddScoped<RadioStationService>()
    .AddScoped<PlaylistService>()
    .AddScoped<MelodeeMetadataMaker>()
    .AddScoped<AlbumRescanEventHandler>()
    .AddScoped<AlbumAddEventHandler>()
    .AddScoped<ILyricPlugin, LyricPlugin>()
    .AddScoped<UserQueueService>();

#endregion

builder.Services.AddSingleton<IBlacklistService, BlacklistService>();

#region Quartz Related

builder.Services.AddQuartz(q => { q.UseTimeZoneConverter(); });
// Resolve IScheduler via factory where needed; avoid blocking sync calls here

builder.Services.AddQuartzServer(opts => { opts.WaitForJobsToComplete = true; });

// Provide IScheduler to DI for services/components that inject it
// Uses the factory to obtain the singleton scheduler instance managed by QuartzServer
builder.Services.AddSingleton<IScheduler>(sp =>
    sp.GetRequiredService<ISchedulerFactory>().GetScheduler().GetAwaiter().GetResult());

#endregion

builder.Services.AddRebus((configurer, provider) =>
{
    return configurer
        .Logging(l => l.Trace())
        .Options(o =>
        {
            o.EnableCompression(32768);
            o.SetNumberOfWorkers(2);
            o.SetMaxParallelism(20);
        })
        .Transport(t => t.UseInMemoryTransport(new InMemNetwork(), "melodee_bus"))
        .Sagas(s => s.StoreInMemory())
        .Timeouts(t => t.StoreInMemory());
});
builder.Services.AddRebusHandler<AlbumAddEventHandler>();
builder.Services.AddRebusHandler<AlbumRescanEventHandler>();
builder.Services.AddRebusHandler<ArtistRescanEventHandler>();
builder.Services.AddRebusHandler<MelodeeAlbumReprocessEventHandler>();
builder.Services.AddRebusHandler<SearchHistoryEventHandler>();
builder.Services.AddRebusHandler<UserLoginEventHandler>();
builder.Services.AddRebusHandler<UserStreamEventHandler>();

builder.WebHost.UseSetting("DetailedErrors", "true");

builder.Services.AddScoped<IStartupMelodeeConfigurationService, StartupMelodeeConfigurationService>();


var app = builder.Build();

// Use forwarded headers for reverse proxy FIRST (only if enabled)
if (useForwardedHeaders)
{
    app.UseForwardedHeaders();
}

// Enable response compression early in the pipeline
app.UseResponseCompression();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", true);
    app.UseHsts(); // HSTS configured via services above
}

app.UseStatusCodePagesWithRedirects("/Error");

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    // Enable HTTPS redirection (addresses Lighthouse: HTTPS issues)
    app.UseHttpsRedirection();   
}


#region Scheduling Quartz Jobs with Configuration

var isQuartzDisabled = SafeParser.ToBoolean(builder.Configuration[AppSettingsKeys.QuartzDisabled]);
if (!isQuartzDisabled)
{
    var quartzSchedulerFactory = app.Services.GetRequiredService<ISchedulerFactory>();
    var quartzScheduler = await quartzSchedulerFactory.GetScheduler();
    var melodeeConfigurationFactory = app.Services.GetRequiredService<IMelodeeConfigurationFactory>();
    var melodeeConfiguration = await melodeeConfigurationFactory.GetConfigurationAsync();

    var artistHousekeepingCronExpression = melodeeConfiguration.GetValue<string>(SettingRegistry.JobsArtistHousekeepingCronExpression);
    if (artistHousekeepingCronExpression.Nullify() != null)
    {
        await quartzScheduler.ScheduleJob(
            JobBuilder.Create<ArtistHousekeepingJob>()
                .WithIdentity(JobKeyRegistry.ArtistHousekeepingJobJobKey)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity("ArtistHousekeepingJobJobKey-trigger")
                .WithCronSchedule(artistHousekeepingCronExpression!)
                .StartNow()
                .Build());
    }

    var artistSearchEngineHousekeepingCronExpression = melodeeConfiguration.GetValue<string>(SettingRegistry.JobsArtistSearchEngineHousekeepingCronExpression);
    if (artistSearchEngineHousekeepingCronExpression.Nullify() != null)
    {
        await quartzScheduler.ScheduleJob(
            JobBuilder.Create<ArtistSearchEngineRepositoryHousekeepingJob>()
                .WithIdentity(JobKeyRegistry.ArtistSearchEngineHousekeepingJobJobKey)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity("ArtistSearchEngineHousekeepingJobJobKey-trigger")
                .WithCronSchedule(artistSearchEngineHousekeepingCronExpression!)
                .StartNow()
                .Build());
    }

    var libraryInboundProcessJobKeyCronExpression = melodeeConfiguration.GetValue<string>(SettingRegistry.JobsLibraryProcessCronExpression);
    if (libraryInboundProcessJobKeyCronExpression.Nullify() != null)
    {
        await quartzScheduler.ScheduleJob(
            JobBuilder.Create<LibraryInboundProcessJob>()
                .WithIdentity(JobKeyRegistry.LibraryInboundProcessJobKey)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity("LibraryInboundProcessJob-trigger")
                .UsingJobData(JobMapNameRegistry.ScanStatus, ScanStatus.Idle.ToString())
                .UsingJobData(JobMapNameRegistry.Count, 0)
                .WithCronSchedule(libraryInboundProcessJobKeyCronExpression!)
                .StartNow()
                .Build());
    }

    var libraryInsertCronExpression = melodeeConfiguration.GetValue<string>(SettingRegistry.JobsLibraryInsertCronExpression);
    if (libraryInsertCronExpression.Nullify() != null)
    {
        await quartzScheduler.ScheduleJob(
            JobBuilder.Create<LibraryInsertJob>()
                .WithIdentity(JobKeyRegistry.LibraryProcessJobJobKey)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity("LibraryProcessJob-trigger")
                .UsingJobData(JobMapNameRegistry.ScanStatus, ScanStatus.Idle.ToString())
                .UsingJobData(JobMapNameRegistry.Count, 0)
                .WithCronSchedule(libraryInsertCronExpression!)
                .StartNow()
                .Build());
    }

    var musicBrainzUpdateDatabaseCronExpression = melodeeConfiguration.GetValue<string>(SettingRegistry.JobsMusicBrainzUpdateDatabaseCronExpression);
    if (musicBrainzUpdateDatabaseCronExpression.Nullify() != null)
    {
        await quartzScheduler.ScheduleJob(
            JobBuilder.Create<MusicBrainzUpdateDatabaseJob>()
                .WithIdentity(JobKeyRegistry.MusicBrainzUpdateDatabaseJobKey)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity("MusicBrainzUpdateDatabaseJob-trigger")
                .WithCronSchedule(musicBrainzUpdateDatabaseCronExpression!)
                .StartNow()
                .Build());
    }
}

#endregion

app.UseCookiePolicy(new CookiePolicyOptions
{
    Secure = CookieSecurePolicy.Always,
    MinimumSameSitePolicy = SameSiteMode.Strict
});

app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? string.Empty);
        diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
    };
});

// Configure static files with efficient caching (addresses Lighthouse: Use efficient cache lifetimes)
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Cache static files for 1 year
        const int durationInSeconds = 60 * 60 * 24 * 365; // 1 year
        ctx.Context.Response.Headers.CacheControl = $"public,max-age={durationInSeconds}";
        
        // Add ETag for better cache validation
        ctx.Context.Response.Headers.ETag = $"\"{ctx.File.LastModified:yyyyMMddHHmmss}\"";
        
        // Add security headers
        ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        
        // Add Content Security Policy (addresses Lighthouse: CSP XSS protection)
        ctx.Context.Response.Headers["Content-Security-Policy"] = 
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-eval' 'unsafe-inline'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: blob:; " +
            "font-src 'self'; " +
            "connect-src 'self' wss: ws:; " +
            "media-src 'self'; " +
            "object-src 'none'; " +
            "frame-ancestors 'self';";
    }
});

app.UseAntiforgery();

// Add security headers to all responses
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.UseCors(bb => bb.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

app.UseMelodeeBlazorHeader();

app.MapControllers();

// Map health checks for readiness/liveness probes
app.MapHealthChecks("/health");

using (var scope = app.Services.CreateScope())
{
    var configService = scope.ServiceProvider.GetRequiredService<IStartupMelodeeConfigurationService>();
    await configService.UpdateConfigurationFromEnvironmentAsync();
}

app.Run();
