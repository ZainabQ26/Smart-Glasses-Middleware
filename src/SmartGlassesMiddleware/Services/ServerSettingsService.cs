using System.Globalization;
using System.Threading;
using Microsoft.Extensions.Configuration;

namespace SmartGlassesMiddleware.Services;

public sealed record PreventSleepSettingState(bool Enabled, bool AutoDisabledAt2AmEt, DateTime RetrievedAtUtc);
public sealed record GroqCommandTranscriptionSettingState(bool Enabled, DateTime RetrievedAtUtc);

public interface IServerSettingsService
{
    Task<PreventSleepSettingState> GetPreventSleepStateAsync(CancellationToken cancellationToken = default);
    Task<PreventSleepSettingState> SetPreventSleepEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    Task<GroqCommandTranscriptionSettingState> GetGroqCommandTranscriptionSettingAsync(CancellationToken cancellationToken = default);
    Task<GroqCommandTranscriptionSettingState> SetGroqCommandTranscriptionEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    Task EnsureDailyResetAppliedAsync(CancellationToken cancellationToken = default);
}

public class ServerSettingsService : IServerSettingsService
{
    private const string PreventSleepEnabledKey = "admin:setting:prevent-sleep:enabled";
    private const string PreventSleepLastResetEtDateKey = "admin:setting:prevent-sleep:last-reset-et-date";
    private static volatile bool _groqCommandTranscriptionEnabled = true;
    private static int _groqSettingInitialized;

    private readonly ICacheService _cache;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ServerSettingsService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _easternTimeZone;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ServerSettingsService(ICacheService cache, IConfiguration configuration, ILogger<ServerSettingsService> logger, TimeProvider? timeProvider = null)
    {
        _cache = cache;
        _configuration = configuration;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _easternTimeZone = ResolveEasternTimeZone();
    }

    public ServerSettingsService(ICacheService cache, ILogger<ServerSettingsService> logger, TimeProvider? timeProvider = null)
        : this(cache, new ConfigurationBuilder().Build(), logger, timeProvider)
    {
    }

    public async Task<PreventSleepSettingState> GetPreventSleepStateAsync(CancellationToken cancellationToken = default)
    {
        var (enabled, autoDisabled) = await EvaluatePreventSleepStateAsync(cancellationToken: cancellationToken);
        return new PreventSleepSettingState(enabled, autoDisabled, _timeProvider.GetUtcNow().UtcDateTime);
    }

    public async Task<PreventSleepSettingState> SetPreventSleepEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var (finalEnabled, autoDisabled) = await EvaluatePreventSleepStateAsync(
            desiredEnabled: enabled,
            persistDesiredValue: true,
            cancellationToken: cancellationToken);

        return new PreventSleepSettingState(finalEnabled, autoDisabled, _timeProvider.GetUtcNow().UtcDateTime);
    }

    public async Task EnsureDailyResetAppliedAsync(CancellationToken cancellationToken = default)
    {
        await EvaluatePreventSleepStateAsync(cancellationToken: cancellationToken);
    }

    public Task<GroqCommandTranscriptionSettingState> GetGroqCommandTranscriptionSettingAsync(CancellationToken cancellationToken = default)
    {
        EnsureGroqSettingInitialized();
        return Task.FromResult(new GroqCommandTranscriptionSettingState(_groqCommandTranscriptionEnabled, _timeProvider.GetUtcNow().UtcDateTime));
    }

    public Task<GroqCommandTranscriptionSettingState> SetGroqCommandTranscriptionEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        EnsureGroqSettingInitialized();
        _groqCommandTranscriptionEnabled = enabled;
        return Task.FromResult(new GroqCommandTranscriptionSettingState(_groqCommandTranscriptionEnabled, _timeProvider.GetUtcNow().UtcDateTime));
    }

    private async Task<(bool enabled, bool autoDisabled)> EvaluatePreventSleepStateAsync(
        bool? desiredEnabled = null,
        bool persistDesiredValue = false,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var nowEastern = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _easternTimeZone);
            var todayEastern = nowEastern.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var enabled = (await _cache.GetAsync<bool?>(PreventSleepEnabledKey)) ?? false;
            var lastResetDate = await _cache.GetAsync<string>(PreventSleepLastResetEtDateKey);
            var autoDisabled = false;

            // Enforce the daily reset when local Eastern time is at/after 2:00 AM.
            if (nowEastern.Hour >= 2 && !string.Equals(lastResetDate, todayEastern, StringComparison.Ordinal))
            {
                if (enabled)
                {
                    enabled = false;
                    autoDisabled = true;
                    await _cache.SetAsync(PreventSleepEnabledKey, false);
                    _logger.LogInformation("Prevent-sleep setting auto-disabled at/after 2:00 AM ET");
                }

                await _cache.SetAsync(PreventSleepLastResetEtDateKey, todayEastern);
            }

            if (persistDesiredValue && desiredEnabled.HasValue)
            {
                enabled = desiredEnabled.Value;
                await _cache.SetAsync(PreventSleepEnabledKey, enabled);
            }

            return (enabled, autoDisabled);
        }
        finally
        {
            _gate.Release();
        }
    }

    private TimeZoneInfo ResolveEasternTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
            catch
            {
                _logger.LogWarning("Could not resolve Eastern timezone; falling back to UTC for prevent-sleep reset");
                return TimeZoneInfo.Utc;
            }
        }
    }

    private void EnsureGroqSettingInitialized()
    {
        if (Interlocked.Exchange(ref _groqSettingInitialized, 1) != 0)
        {
            return;
        }

        var configured = _configuration["AI:Groq:Enabled"]?.Trim();
        if (bool.TryParse(configured, out var enabled))
        {
            _groqCommandTranscriptionEnabled = enabled;
        }
        else
        {
            _groqCommandTranscriptionEnabled = true;
        }
    }
}

public class PreventSleepResetBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PreventSleepResetBackgroundService> _logger;
    private readonly TimeProvider _timeProvider;

    public PreventSleepResetBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<PreventSleepResetBackgroundService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var settingsService = scope.ServiceProvider.GetRequiredService<IServerSettingsService>();
                await settingsService.EnsureDailyResetAppliedAsync(stoppingToken);
                await Task.Delay(PollInterval, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Prevent-sleep reset loop failed; retrying shortly");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), _timeProvider, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
