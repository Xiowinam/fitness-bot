
using FitnessBot.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Конфигурация
        services.Configure<BotConfiguration>(
            context.Configuration.GetSection("BotConfiguration"));

        // Сервисы
        services.AddSingleton<DatabaseService>(provider =>
        {
            var connectionString = context.Configuration.GetConnectionString("PostgreSQL");
            return new DatabaseService(connectionString);
        });

        services.AddSingleton<TelegramBotService>(provider =>
        {
            var botConfig = context.Configuration.GetSection("BotConfiguration").Get<BotConfiguration>();
            var dbService = provider.GetRequiredService<DatabaseService>();
            return new TelegramBotService(botConfig.BotToken, dbService);
        });

        services.AddHostedService<BotWorker>();
    })
    .Build();

await host.RunAsync();

public class BotConfiguration
{
    public string BotToken { get; set; } = string.Empty;
}

public class BotWorker : IHostedService
{
    private readonly DatabaseService _dbService;
    private readonly TelegramBotService _botService;

    public BotWorker(DatabaseService dbService, TelegramBotService botService)
    {
        _dbService = dbService;
        _botService = botService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Инициализация базы данных...");
        await _dbService.InitializeDatabaseAsync();

        Console.WriteLine("Запуск бота...");
        await _botService.StartBotAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Бот остановлен");
        return Task.CompletedTask;
    }
}