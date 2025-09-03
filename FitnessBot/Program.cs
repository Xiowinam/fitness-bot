using FitnessBot.Services;
using FitnessBot.Models;
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

        // Логирование
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
        });

        // Получаем строку подключения
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSQL")
            ?? context.Configuration.GetConnectionString("PostgreSQL");

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("Database connection string is not configured");
        }

        // Сервисы
        services.AddSingleton<TelegramBotService>(provider =>
        {
            var botConfig = context.Configuration.GetSection("BotConfiguration").Get<BotConfiguration>();
            if (string.IsNullOrEmpty(botConfig?.BotToken))
            {
                throw new InvalidOperationException("Bot token is not configured");
            }

            var dbService = provider.GetRequiredService<DatabaseService>();
            var logger = provider.GetRequiredService<ILogger<TelegramBotService>>();
            return new TelegramBotService(botConfig.BotToken, dbService, logger);
        });

        services.AddSingleton<TelegramBotService>(provider =>
        {
            var botConfig = context.Configuration.GetSection("BotConfiguration").Get<BotConfiguration>();
            if (string.IsNullOrEmpty(botConfig?.BotToken))
            {
                throw new InvalidOperationException("Bot token is not configured");
            }

            var dbService = provider.GetRequiredService<DatabaseService>();
            var logger = provider.GetRequiredService<ILogger<TelegramBotService>>();
            return new TelegramBotService(botConfig.BotToken, dbService, logger);
        });

        services.AddHostedService<BotWorker>();
    })
    .Build();

// Инициализация базы данных
try
{
    using var scope = host.Services.CreateScope();
    var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
    await dbService.InitializeDatabaseAsync();
    Console.WriteLine("Database initialized successfully");
}
catch (Exception ex)
{
    Console.WriteLine($"Error initializing database: {ex.Message}");
    throw;
}

await host.RunAsync();

public class BotConfiguration
{
    public string BotToken { get; set; } = string.Empty;
}

public class BotWorker : IHostedService
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

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting bot worker...");
            await _botService.StartBotAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting bot worker");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Bot worker stopped");
        return Task.CompletedTask;
    }
}