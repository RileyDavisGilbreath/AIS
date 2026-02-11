namespace AlabamaWalkabilityApi.Services;

public class ScheduledImportService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledImportService> _logger;
    private readonly TimeSpan _interval;
    private readonly string? _csvUrl;

    public ScheduledImportService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<ScheduledImportService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = TimeSpan.FromHours(config.GetValue("Import:IntervalHours", 24));
        _csvUrl = config["Import:CsvUrl"];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(_csvUrl))
        {
            _logger.LogInformation("Scheduled import disabled: Import:CsvUrl not set in appsettings.");
            return;
        }

        _logger.LogInformation("Scheduled import enabled. Interval: {Interval}. Running import once at startup.", _interval);

        // Run once at startup so DB is populated immediately
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var import = scope.ServiceProvider.GetRequiredService<IWalkabilityImportService>();
                var (blockGroups, counties) = await import.ImportFromUrlAsync(_csvUrl, stoppingToken);
                _logger.LogInformation("Startup import completed: {BlockGroups} block groups, {Counties} counties.", blockGroups, counties);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup import failed. Scheduled runs will retry every {Interval}.", _interval);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var import = scope.ServiceProvider.GetRequiredService<IWalkabilityImportService>();
                var (blockGroups, counties) = await import.ImportFromUrlAsync(_csvUrl, stoppingToken);
                _logger.LogInformation("Scheduled import completed: {BlockGroups} block groups, {Counties} counties.", blockGroups, counties);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled import failed.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
