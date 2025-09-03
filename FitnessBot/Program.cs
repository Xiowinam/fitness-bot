using FitnessBot.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

Console.WriteLine("=== Fitness Bot Starting ===");

// Получаем переменные окружения
var botToken = Environment.GetEnvironmentVariable("BOT_TOKEN");
var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL");

Console.WriteLine($"Bot token: {!string.IsNullOrEmpty(botToken)}");
Console.WriteLine($"Connection string: {!string.IsNullOrEmpty(connectionString)}");

if (string.IsNullOrEmpty(botToken))
{
    Console.WriteLine("ERROR: BOT_TOKEN environment variable is required");
    return;
}

if (string.IsNullOrEmpty(connectionString))
{
    Console.WriteLine("ERROR: DATABASE_URL environment variable is required");
    return;
}

try
{
    var host = Host.CreateDefaultBuilder(args)
        .ConfigureServices(services =>
        {
            services.AddSingleton(new DatabaseService(connectionString));
            services.AddSingleton(new TelegramBotService(botToken, services.BuildServiceProvider().GetRequiredService<DatabaseService>()));
            services.AddHostedService<BotWorker>();
        })
        .Build();

    // Инициализация базы
    var dbService = host.Services.GetRequiredService<DatabaseService>();
    await dbService.InitializeDatabaseAsync();

    Console.WriteLine("✅ Database initialized successfully");
    Console.WriteLine("✅ Starting bot...");

    await host.RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Fatal error: {ex.Message}");
    Environment.Exit(1);
}