using FitnessBot.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Конфигурация
        services.Configure<BotConfiguration>(
            context.Configuration.GetSection("BotConfiguration"));

        // Сервис базы данных
        services.AddSingleton<DatabaseService>(provider =>
        {
            var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL");
            if (string.IsNullOrEmpty(connectionString))
            {
                connectionString = context.Configuration.GetConnectionString("PostgreSQL");
            }

            var logger = provider.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Инициализация DatabaseService");

            return new DatabaseService(connectionString);
        });

        // Сервис телеграм бота
        services.AddSingleton<TelegramBotService>(provider =>
        {
            var botConfig = context.Configuration.GetSection("BotConfiguration").Get<BotConfiguration>();
            var dbService = provider.GetRequiredService<DatabaseService>();
            return new TelegramBotService(botConfig.BotToken, dbService);
        });

        services.AddHostedService<BotWorker>();
    })
    .Build();

// Инициализация базы данных перед запуском
using (var scope = host.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var dbService = services.GetRequiredService<DatabaseService>();

    try
    {
        logger.LogInformation("Инициализация базы данных...");
        await dbService.InitializeDatabaseAsync();
        logger.LogInformation("База данных инициализирована");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Ошибка инициализации базы данных");
        throw;
    }
}

await host.RunAsync();

public class BotConfiguration
{
    public string BotToken { get; set; } = string.Empty;
}

public class BotWorker : BackgroundService
{
    private readonly DatabaseService _dbService;
    private readonly TelegramBotService _botService;
    private readonly ILogger<BotWorker> _logger;

    public BotWorker(DatabaseService dbService, TelegramBotService botService, ILogger<BotWorker> logger)
    {
        _dbService = dbService;
        _botService = botService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Запуск бота...");
        await _botService.StartBotAsync(stoppingToken);

        // Ждем отмены
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Остановка бота...");
        await base.StopAsync(cancellationToken);
    }
}