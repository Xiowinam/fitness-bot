using FitnessBot.Services;
using Microsoft.Extensions.Hosting;

Console.WriteLine("=== Fitness Bot Starting ===");

// Получаем переменные окружения
var botToken = Environment.GetEnvironmentVariable("BOT_TOKEN");
var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL"); // Используем как есть

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

// Выводим для отладки (первые 100 символов)
Console.WriteLine($"Connection string preview: {connectionString.Substring(0, Math.Min(100, connectionString.Length))}");

try
{
    // Создаем сервисы
    var dbService = new DatabaseService(connectionString);
    var botService = new TelegramBotService(botToken, dbService);

    // Инициализация базы
    await dbService.InitializeDatabaseAsync();
    Console.WriteLine("✅ Database initialized successfully");

    // Запуск бота
    Console.WriteLine("✅ Starting bot...");
    using var cts = new CancellationTokenSource();
    await botService.StartBotAsync(cts.Token);

    // Ждем пока работает бот
    Console.WriteLine("Bot is running. Press Ctrl+C to stop.");
    await Task.Delay(-1, cts.Token);
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Fatal error: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
    Environment.Exit(1);
}