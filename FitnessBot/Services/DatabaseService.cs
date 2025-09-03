using Npgsql;
using FitnessBot.Models;

namespace FitnessBot.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService(string connectionString)
        {
            _connectionString = connectionString;
            Console.WriteLine("DatabaseService created");
        }

        public async Task InitializeDatabaseAsync()
        {
            try
            {
                Console.WriteLine("Initializing database...");
                Console.WriteLine($"Using connection string: {_connectionString}");

                // Пробуем разные форматы подключения
                NpgsqlConnection connection;

                if (_connectionString.Contains("postgresql://"))
                {
                    // Парсим Render-style URL
                    connection = ParseRenderConnectionString(_connectionString);
                }
                else
                {
                    // Используем как есть (стандартный формат .NET)
                    connection = new NpgsqlConnection(_connectionString);
                }

                using (connection)
                {
                    await connection.OpenAsync();
                    Console.WriteLine("✅ Connected to database");

                    var command = new NpgsqlCommand(@"
                CREATE TABLE IF NOT EXISTS users (
                    user_id BIGSERIAL PRIMARY KEY,
                    telegram_id BIGINT UNIQUE NOT NULL,
                    username VARCHAR(100),
                    first_name VARCHAR(255),
                    last_name VARCHAR(255),
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );

                CREATE TABLE IF NOT EXISTS user_parameters (
                    param_id BIGSERIAL PRIMARY KEY,
                    user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
                    gender VARCHAR(20),
                    age INTEGER,
                    weight DECIMAL(5, 2),
                    height INTEGER,
                    goal VARCHAR(50),
                    activity_level VARCHAR(50),
                    daily_calories INTEGER,
                    protein_goal INTEGER,
                    fat_goal INTEGER,
                    carbs_goal INTEGER,
                    workout_plan_text TEXT,
                    diet_advice TEXT,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );
            ", connection);

                    await command.ExecuteNonQueryAsync();
                    Console.WriteLine("✅ Database tables created");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Database error: {ex.Message}");
                throw;
            }
        }

        private NpgsqlConnection ParseRenderConnectionString(string renderUrl)
        {
            try
            {
                Console.WriteLine("Parsing Render-style connection string...");
                Console.WriteLine($"Original URL: {renderUrl}");

                // Убираем префикс
                var withoutPrefix = renderUrl.Replace("postgresql://", "");
                Console.WriteLine($"Without prefix: {withoutPrefix}");

                // Разделяем на части по @
                var atIndex = withoutPrefix.IndexOf('@');
                if (atIndex == -1) throw new FormatException("No @ symbol found in connection string");

                var userPassPart = withoutPrefix.Substring(0, atIndex);
                var hostDbPart = withoutPrefix.Substring(atIndex + 1);

                Console.WriteLine($"UserPass part: {userPassPart}");
                Console.WriteLine($"HostDB part: {hostDbPart}");

                // Парсим username:password
                var colonIndex = userPassPart.IndexOf(':');
                if (colonIndex == -1) throw new FormatException("No : symbol in user:password part");

                var username = userPassPart.Substring(0, colonIndex);
                var password = userPassPart.Substring(colonIndex + 1);

                Console.WriteLine($"Username: {username}, Password: {password}");

                // Парсим host/database
                var slashIndex = hostDbPart.IndexOf('/');
                if (slashIndex == -1) throw new FormatException("No / symbol in host/database part");

                var host = hostDbPart.Substring(0, slashIndex);
                var database = hostDbPart.Substring(slashIndex + 1);
                var port = 5432; // Стандартный порт PostgreSQL

                Console.WriteLine($"Host: {host}, Database: {database}, Port: {port}");

                // Создаем стандартную строку подключения .NET
                var netConnectionString = $"Host={host};Port={port};Username={username};Password={password};Database={database};SSL Mode=Require;Trust Server Certificate=true";

                Console.WriteLine($"Converted to: {netConnectionString}");
                return new NpgsqlConnection(netConnectionString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing connection string: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        public async Task<long> GetOrCreateUserAsync(long telegramId, string? firstName, string? lastName, string? username)
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            var findCommand = new NpgsqlCommand("SELECT user_id FROM users WHERE telegram_id = @telegramId", connection);
            findCommand.Parameters.AddWithValue("telegramId", telegramId);

            var result = await findCommand.ExecuteScalarAsync();
            if (result != null) return (long)result;

            var insertCommand = new NpgsqlCommand(@"
                INSERT INTO users (telegram_id, username, first_name, last_name)
                VALUES (@telegramId, @username, @firstName, @lastName)
                RETURNING user_id;", connection);

            insertCommand.Parameters.AddWithValue("telegramId", telegramId);
            insertCommand.Parameters.AddWithValue("username", (object?)username ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("firstName", (object?)firstName ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("lastName", (object?)lastName ?? DBNull.Value);

            return (long)await insertCommand.ExecuteScalarAsync();
        }

        public async Task SaveUserParametersAsync(long userId, UserParameters parameters)
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = new NpgsqlCommand(@"
                INSERT INTO user_parameters 
                (user_id, gender, age, weight, height, goal, activity_level, daily_calories, protein_goal, fat_goal, carbs_goal, workout_plan_text, diet_advice)
                VALUES 
                (@userId, @gender, @age, @weight, @height, @goal, @activityLevel, @calories, @protein, @fat, @carbs, @workoutPlan, @dietAdvice)", connection);

            command.Parameters.AddWithValue("userId", userId);
            command.Parameters.AddWithValue("gender", parameters.Gender);
            command.Parameters.AddWithValue("age", parameters.Age);
            command.Parameters.AddWithValue("weight", parameters.Weight);
            command.Parameters.AddWithValue("height", parameters.Height);
            command.Parameters.AddWithValue("goal", parameters.Goal);
            command.Parameters.AddWithValue("activityLevel", parameters.ActivityLevel);
            command.Parameters.AddWithValue("calories", parameters.DailyCalories);
            command.Parameters.AddWithValue("protein", parameters.ProteinGoal);
            command.Parameters.AddWithValue("fat", parameters.FatGoal);
            command.Parameters.AddWithValue("carbs", parameters.CarbsGoal);
            command.Parameters.AddWithValue("workoutPlan", parameters.WorkoutPlan);
            command.Parameters.AddWithValue("dietAdvice", parameters.DietAdvice);

            await command.ExecuteNonQueryAsync();
        }

        // Простые версии недостающих методов
        public async Task<bool> UserHasProfileAsync(long telegramId)
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                var command = new NpgsqlCommand(@"
            SELECT COUNT(*) FROM user_parameters up
            JOIN users u ON u.user_id = up.user_id
            WHERE u.telegram_id = @telegramId", connection);

                command.Parameters.AddWithValue("telegramId", telegramId);
                var count = (long)(await command.ExecuteScalarAsync() ?? 0);
                return count > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking user profile: {ex.Message}");
                return false;
            }
        }

        public async Task<UserParameters?> GetLatestUserParametersAsync(long telegramId)
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                var command = new NpgsqlCommand(@"
            SELECT up.gender, up.age, up.weight, up.height, up.goal, up.activity_level, 
                   up.daily_calories, up.protein_goal, up.fat_goal, up.carbs_goal 
            FROM user_parameters up
            JOIN users u ON u.user_id = up.user_id
            WHERE u.telegram_id = @telegramId
            ORDER BY up.created_at DESC
            LIMIT 1", connection);

                command.Parameters.AddWithValue("telegramId", telegramId);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new UserParameters
                    {
                        Gender = reader.GetString(reader.GetOrdinal("gender")),
                        Age = reader.GetInt32(reader.GetOrdinal("age")),
                        Weight = reader.GetDouble(reader.GetOrdinal("weight")),
                        Height = reader.GetInt32(reader.GetOrdinal("height")),
                        Goal = reader.GetString(reader.GetOrdinal("goal")),
                        ActivityLevel = reader.GetString(reader.GetOrdinal("activity_level")),
                        DailyCalories = reader.GetInt32(reader.GetOrdinal("daily_calories")),
                        ProteinGoal = reader.GetInt32(reader.GetOrdinal("protein_goal")),
                        FatGoal = reader.GetInt32(reader.GetOrdinal("fat_goal")),
                        CarbsGoal = reader.GetInt32(reader.GetOrdinal("carbs_goal"))
                    };
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting user parameters: {ex.Message}");
                return null;
            }
        }
    }
}