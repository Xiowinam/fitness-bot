
using FitnessBot.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Получаем строку подключения из переменных окружения
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrEmpty(connectionString))
        {
            connectionString = context.Configuration.GetConnectionString("PostgreSQL");
        }

        // Сервис базы данных
        services.AddSingleton<DatabaseService>(provider =>
            new DatabaseService(connectionString));

        // Остальные сервисы...
    })
    .Build();

// Проверяем подключение к базе
using var scope = host.Services.CreateScope();
var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
await dbService.InitializeDatabaseAsync();

await host.RunAsync();