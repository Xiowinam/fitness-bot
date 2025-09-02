using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using FitnessBot.Models;

namespace FitnessBot.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;

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
                Console.WriteLine($"Ошибка в GetLatestUserParametersAsync: {ex.Message}");
                return null;
            }
        }

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
                Console.WriteLine($"Профилей найдено для {telegramId}: {count}");
                return count > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в UserHasProfileAsync: {ex.Message}");
                return false;
            }
        }

        public DatabaseService(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task InitializeDatabaseAsync()
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            // Создаем таблицы если они не существуют
            var createTablesCommand = new NpgsqlCommand(@"
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
                    gender VARCHAR(20) CHECK (gender IN ('male', 'female')),
                    age INTEGER CHECK (age > 0 AND age < 120),
                    weight DECIMAL(5, 2) CHECK (weight > 0 AND weight < 300),
                    height INTEGER CHECK (height > 0 AND height < 250),
                    goal VARCHAR(50) CHECK (goal IN ('weight_loss', 'maintenance', 'weight_gain')),
                    activity_level VARCHAR(50) CHECK (activity_level IN ('sedentary', 'light', 'moderate', 'active', 'very_active')),
                    daily_calories INTEGER,
                    protein_goal INTEGER,
                    fat_goal INTEGER,
                    carbs_goal INTEGER,
                    workout_plan_text TEXT,
                    diet_advice TEXT,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );
            ", connection);

            await createTablesCommand.ExecuteNonQueryAsync();
        }

        public async Task<long> GetOrCreateUserAsync(long telegramId, string? firstName, string? lastName, string? username)
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            // Пытаемся найти пользователя
            var findCommand = new NpgsqlCommand("SELECT user_id FROM users WHERE telegram_id = @telegramId", connection);
            findCommand.Parameters.AddWithValue("telegramId", telegramId);

            var result = await findCommand.ExecuteScalarAsync();
            if (result != null)
            {
                return (long)result;
            }

            // Создаем нового пользователя
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
    }
}
