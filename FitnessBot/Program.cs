using FitnessBot.Services;
using Microsoft.Extensions.Hosting;

Console.WriteLine("=== Fitness Bot Starting ===");

// Получаем переменные окружения
var botToken = Environment.GetEnvironmentVariable("BOT_TOKEN");
var renderDbUrl = Environment.GetEnvironmentVariable("DATABASE_URL");

Console.WriteLine($"Bot token: {!string.IsNullOrEmpty(botToken)}");
Console.WriteLine($"Database URL: {!string.IsNullOrEmpty(renderDbUrl)}");

if (string.IsNullOrEmpty(botToken))
{
    Console.WriteLine("ERROR: BOT_TOKEN environment variable is required");
    return;
}

if (string.IsNullOrEmpty(renderDbUrl))
{
    Console.WriteLine("ERROR: DATABASE_URL environment variable is required");
    return;
}

// Преобразование Render PostgreSQL URL в .NET connection string
var connectionString = ConvertRenderDbUrlToConnectionString(renderDbUrl);
Console.WriteLine($"Converted connection string: {connectionString}");

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

// Метод для преобразования Render DB URL в .NET connection string
static string ConvertRenderDbUrlToConnectionString(string renderDbUrl)
{
    try
    {
        Console.WriteLine($"Converting DB URL: {renderDbUrl}");

        // Удаляем префикс postgresql:// если есть
        if (renderDbUrl.StartsWith("postgresql://"))
        {
            renderDbUrl = renderDbUrl.Replace("postgresql://", "postgres://");
        }

        // Парсим URI
        var uri = new Uri(renderDbUrl);
        var userInfo = uri.UserInfo.Split(':');

        if (userInfo.Length != 2)
        {
            throw new FormatException("Invalid user info format in database URL");
        }

        var username = userInfo[0];
        var password = userInfo[1];
        var host = uri.Host;
        var port = uri.Port;
        var database = uri.AbsolutePath.TrimStart('/');

        Console.WriteLine($"Parsed: Host={host}, Port={port}, User={username}, DB={database}");

        // Формируем connection string
        return $"Host={host};Port={port};Username={username};Password={password};Database={database};SSL Mode=Require;Trust Server Certificate=true";
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error converting database URL: {ex.Message}");
        Console.WriteLine($"URL was: {renderDbUrl}");
        throw;
    }
}