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

                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                Console.WriteLine("✅ Connected to database");

                // Создаем таблицы
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
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Database error: {ex.Message}");
                throw;
            }
        }

        public async Task<long> GetOrCreateUserAsync(long telegramId, string? firstName, string? lastName, string? username)
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            // Поиск пользователя
            var findCommand = new NpgsqlCommand("SELECT user_id FROM users WHERE telegram_id = @telegramId", connection);
            findCommand.Parameters.AddWithValue("telegramId", telegramId);

            var result = await findCommand.ExecuteScalarAsync();
            if (result != null) return (long)result;

            // Создание нового пользователя
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

            // Добавление параметров
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
    }
}