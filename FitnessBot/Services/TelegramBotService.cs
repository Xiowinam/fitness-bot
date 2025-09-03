using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using FitnessBot.Models;
using FitnessBot.Services;
using Telegram.Bot.Polling;
using Telegram.Bot.Exceptions;
using Microsoft.Extensions.Logging;

namespace FitnessBot.Services
{
    public class TelegramBotService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly DatabaseService _dbService;
        private readonly Dictionary<long, UserState> _userStates = new();

        public TelegramBotService(string botToken, DatabaseService dbService)
        {
            _botClient = new TelegramBotClient(botToken);
            _dbService = dbService;
            Console.WriteLine("TelegramBotService initialized");
        }

        public async Task StartBotAsync(CancellationToken cancellationToken)
        {
            var me = await _botClient.GetMeAsync(cancellationToken);
            Console.WriteLine($"Бот @{me.Username} запущен!");

            _botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                pollingErrorHandler: HandlePollingErrorAsync,
                receiverOptions: new ReceiverOptions { AllowedUpdates = [] },
                cancellationToken: cancellationToken
            );

            // Бесконечно ждем пока не отменят
            await Task.Delay(-1, cancellationToken);
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                Console.WriteLine($"=== НОВЫЙ UPDATE ===");

                if (update.Message is not { } message || message.Text is not { } messageText)
                {
                    Console.WriteLine("Сообщение или текст пустые");
                    return;
                }

                var chatId = message.Chat.Id;
                Console.WriteLine($"Чат ID: {chatId}, Текст: {messageText}");

                // Регистрируем пользователя в БД
                try
                {
                    var userId = await _dbService.GetOrCreateUserAsync(
                        chatId, message.Chat.FirstName, message.Chat.LastName, message.Chat.Username);
                    Console.WriteLine($"User ID в БД: {userId}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка БД: {ex.Message}");
                    await botClient.SendTextMessageAsync(chatId, "Ошибка базы данных. Попробуйте позже.", cancellationToken: cancellationToken);
                    return;
                }

                // Получаем или создаем состояние пользователя
                if (!_userStates.TryGetValue(chatId, out var userState))
                {
                    userState = new UserState();
                    _userStates[chatId] = userState;
                    Console.WriteLine("Создано новое состояние для пользователя");
                }

                Console.WriteLine($"Текущее состояние: {userState.CurrentState}");

                // Обрабатываем команды
                if (messageText.StartsWith('/'))
                {
                    await HandleCommandAsync(botClient, message, userState, cancellationToken);
                    return;
                }

                // Обрабатываем ответы в диалоге
                await HandleDialogAsync(botClient, message, userState, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"КРИТИЧЕСКАЯ ОШИБКА в HandleUpdateAsync: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
            }
        }

        private async Task HandleCommandAsync(ITelegramBotClient botClient, Message message, UserState userState, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            var messageText = message.Text;

            Console.WriteLine($"Обработка команды: {messageText}");

            switch (messageText)
            {
                case "/start":
                    await HandleStartCommand(botClient, chatId, userState, cancellationToken);
                    break;

                case "/cancel":
                    userState.Reset();
                    await botClient.SendTextMessageAsync(chatId, "Диалог прерван.", cancellationToken: cancellationToken);
                    await ShowMainMenu(botClient, chatId, cancellationToken);
                    break;

                case "/profile":
                    await ShowUserProfile(botClient, chatId, cancellationToken);
                    break;

                case "/update_weight":
                    await StartWeightUpdate(botClient, chatId, userState, cancellationToken);
                    break;

                case "/new_plan":
                    userState.Reset();
                    await SendGenderKeyboardAsync(botClient, chatId, cancellationToken);
                    break;

                case "/exercises":
                    await ShowExercisesMenu(botClient, chatId, cancellationToken);
                    break;

                default:
                    await botClient.SendTextMessageAsync(chatId, "Неизвестная команда.", cancellationToken: cancellationToken);
                    await ShowMainMenu(botClient, chatId, cancellationToken);
                    break;
            }
        }

        private async Task HandleDialogAsync(ITelegramBotClient botClient, Message message, UserState userState, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            var messageText = message.Text;

            Console.WriteLine($"Обработка диалога - Шаг: '{userState.CurrentState}', Сообщение: '{messageText}'");

            switch (userState.CurrentState)
            {
                case ConversationState.Start:
                    await SendGenderKeyboardAsync(botClient, chatId, cancellationToken);
                    break;

                case ConversationState.AskingGender:
                    if (messageText == "Мужской" || messageText == "Женский")
                    {
                        userState.Gender = messageText.ToLower() == "мужской" ? "male" : "female";
                        userState.CurrentState = ConversationState.AskingAge;
                        Console.WriteLine($"Пол установлен: {userState.Gender}");

                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Отлично! Теперь введи свой возраст (целое число):",
                            replyMarkup: new ReplyKeyboardRemove(),
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        Console.WriteLine($"Неверный выбор пола: {messageText}");
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Пожалуйста, выбери пол из предложенных вариантов:",
                            cancellationToken: cancellationToken);
                        await SendGenderKeyboardAsync(botClient, chatId, cancellationToken);
                    }
                    break;

                case ConversationState.AskingAge:
                    if (int.TryParse(messageText, out int age) && age > 0 && age < 120)
                    {
                        userState.Age = age;
                        userState.CurrentState = ConversationState.AskingWeight;
                        Console.WriteLine($"Возраст установлен: {age}");

                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Хорошо. Теперь введи свой вес в кг (например, 70.5):",
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        Console.WriteLine($"Неверный возраст: {messageText}");
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Пожалуйста, введите корректный возраст (от 1 до 120):",
                            cancellationToken: cancellationToken);
                    }
                    break;

                case ConversationState.AskingWeight:
                    if (double.TryParse(messageText, out double weight) && weight > 0 && weight < 300)
                    {
                        userState.Weight = weight;
                        userState.CurrentState = ConversationState.AskingHeight;
                        Console.WriteLine($"Вес установлен: {weight}");

                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Отлично! Теперь введи свой рост в см (например, 180):",
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        Console.WriteLine($"Неверный вес: {messageText}");
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Пожалуйста, введите корректный вес (например, 70.5):",
                            cancellationToken: cancellationToken);
                    }
                    break;

                case ConversationState.AskingHeight:
                    if (int.TryParse(messageText, out int height) && height > 0 && height < 250)
                    {
                        userState.Height = height;
                        userState.CurrentState = ConversationState.AskingGoal;
                        Console.WriteLine($"Рост установлен: {height}");

                        await SendGoalKeyboardAsync(botClient, chatId, cancellationToken);
                    }
                    else
                    {
                        Console.WriteLine($"Неверный рост: {messageText}");
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Пожалуйста, введите корректный рост (например, 180):",
                            cancellationToken: cancellationToken);
                    }
                    break;

                case ConversationState.AskingGoal:
                    userState.Goal = messageText switch
                    {
                        "Похудение" => "weight_loss",
                        "Поддержание веса" => "maintenance",
                        "Набор массы" => "weight_gain",
                        _ => null
                    };

                    if (userState.Goal != null)
                    {
                        userState.CurrentState = ConversationState.AskingActivity;
                        Console.WriteLine($"Цель установлена: {userState.Goal}");

                        await SendActivityKeyboardAsync(botClient, chatId, cancellationToken);
                    }
                    else
                    {
                        Console.WriteLine($"Неверная цель: {messageText}");
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Пожалуйста, выбери цель из предложенных вариантов:",
                            cancellationToken: cancellationToken);
                        await SendGoalKeyboardAsync(botClient, chatId, cancellationToken);
                    }
                    break;

                case ConversationState.AskingActivity:
                    userState.ActivityLevel = messageText switch
                    {
                        "Сидячий" => "sedentary",
                        "Легкая активность" => "light",
                        "Умеренная активность" => "moderate",
                        "Высокая активность" => "active",
                        "Очень высокая активность" => "very_active",
                        _ => null
                    };

                    if (userState.ActivityLevel != null)
                    {
                        userState.CurrentState = ConversationState.ProcessingResults;
                        Console.WriteLine($"Активность установлена: {userState.ActivityLevel}");

                        await ProcessResultsAsync(botClient, chatId, userState, cancellationToken);
                    }
                    else
                    {
                        Console.WriteLine($"Неверная активность: {messageText}");
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Пожалуйста, выбери уровень активности из предложенных вариантов:",
                            cancellationToken: cancellationToken);
                        await SendActivityKeyboardAsync(botClient, chatId, cancellationToken);
                    }
                    break;

                case ConversationState.MainMenu:
                    Console.WriteLine("Обработка главного меню");
                    if (messageText == "📊 Мой профиль")
                    {
                        await ShowUserProfile(botClient, chatId, cancellationToken);
                    }
                    else if (messageText == "⚖️ Обновить вес")
                    {
                        await StartWeightUpdate(botClient, chatId, userState, cancellationToken);
                    }
                    else if (messageText == "🎯 Новый план")
                    {
                        userState.Reset();
                        await SendGenderKeyboardAsync(botClient, chatId, cancellationToken);
                    }
                    else if (messageText == "📋 Список упражнений")
                    {
                        await ShowExercisesMenu(botClient, chatId, cancellationToken);
                    }
                    else if (messageText == "❌ Отмена")
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Главное меню закрыто. Напишите /start для перезахода.",
                            replyMarkup: new ReplyKeyboardRemove(),
                            cancellationToken: cancellationToken);
                        userState.CurrentState = ConversationState.Start;
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Пожалуйста, используйте кнопки меню:",
                            cancellationToken: cancellationToken);
                        await ShowMainMenu(botClient, chatId, cancellationToken);
                    }
                    break;

                case ConversationState.UpdatingWeight:
                    if (double.TryParse(messageText, out double newWeight) && newWeight > 0 && newWeight < 300)
                    {
                        userState.Weight = newWeight;

                        // Пересчитываем план с новым весом
                        var parameters = CalculateFitnessPlan(userState);

                        // Сохраняем в БД
                        var userId = await _dbService.GetOrCreateUserAsync(chatId, null, null, null);
                        await _dbService.SaveUserParametersAsync(userId, parameters);

                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: $"✅ Вес обновлен на {newWeight} кг\nНовые рекомендации рассчитаны!",
                            cancellationToken: cancellationToken);

                        // Показываем обновленный профиль
                        await ShowUserProfile(botClient, chatId, cancellationToken);

                        userState.IsUpdatingWeight = false;
                        await ShowMainMenu(botClient, chatId, cancellationToken);
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Пожалуйста, введите корректный вес (например, 70.5):",
                            cancellationToken: cancellationToken);
                    }
                    break;

                case ConversationState.ShowingExercisesMenu:
                    if (messageText == "🏃‍♂️ Упражнения для похудения")
                    {
                        await ShowWeightLossExercises(botClient, chatId, cancellationToken);
                    }
                    else if (messageText == "💪 Упражнения для набора массы")
                    {
                        await ShowMassGainExercises(botClient, chatId, cancellationToken);
                    }
                    else if (messageText == "⚖️ Упражнения для поддержания")
                    {
                        await ShowMaintenanceExercises(botClient, chatId, cancellationToken);
                    }
                    else if (messageText == "↩️ Назад в меню")
                    {
                        await ShowMainMenu(botClient, chatId, cancellationToken);
                    }
                    else
                    {
                        await ShowExercisesMenu(botClient, chatId, cancellationToken);
                    }
                    break;

                default:
                    Console.WriteLine($"Неизвестное состояние: {userState.CurrentState}");
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Напишите /start чтобы начать заново.",
                        cancellationToken: cancellationToken);
                    break;
            }
        }

        private async Task SendGenderKeyboardAsync(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            try
            {
                var keyboard = new ReplyKeyboardMarkup(new[]
                {
                    new[] { new KeyboardButton("Мужской"), new KeyboardButton("Женский") }
                })
                {
                    ResizeKeyboard = true,
                    OneTimeKeyboard = true
                };

                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Привет! Я твой фитнес-помощник. Для начала скажи, какой у тебя пол?",
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);

                if (_userStates.TryGetValue(chatId, out var userState))
                {
                    userState.CurrentState = ConversationState.AskingGender;
                    Console.WriteLine("Установлено состояние: AskingGender");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки клавиатуры пола: {ex.Message}");
            }
        }

        private async Task SendGoalKeyboardAsync(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            try
            {
                var keyboard = new ReplyKeyboardMarkup(new[]
                {
                    new[] { new KeyboardButton("Похудение") },
                    new[] { new KeyboardButton("Поддержание веса") },
                    new[] { new KeyboardButton("Набор массы") }
                })
                {
                    ResizeKeyboard = true,
                    OneTimeKeyboard = true
                };

                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Какова твоя цель?",
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);

                if (_userStates.TryGetValue(chatId, out var userState))
                {
                    userState.CurrentState = ConversationState.AskingGoal;
                    Console.WriteLine("Установлено состояние: AskingGoal");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки клавиатуры цели: {ex.Message}");
            }
        }

        private async Task SendActivityKeyboardAsync(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            try
            {
                var keyboard = new ReplyKeyboardMarkup(new[]
                {
                    new[] { new KeyboardButton("Сидячий") },
                    new[] { new KeyboardButton("Легкая активность") },
                    new[] { new KeyboardButton("Умеренная активность") },
                    new[] { new KeyboardButton("Высокая активность") },
                    new[] { new KeyboardButton("Очень высокая активность") }
                })
                {
                    ResizeKeyboard = true,
                    OneTimeKeyboard = true
                };

                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Какой у тебя уровень активности?\n\n" +
                          "• Сидячий: офисная работа, нет спорта\n" +
                          "• Легкая: 1-2 тренировки в неделю\n" +
                          "• Умеренная: 3-4 тренировки в неделю\n" +
                          "• Высокая: 5-6 тренировок в неделю\n" +
                          "• Очень высокая: профессиональный спорт",
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);

                if (_userStates.TryGetValue(chatId, out var userState))
                {
                    userState.CurrentState = ConversationState.AskingActivity;
                    Console.WriteLine("Установлено состояние: AskingActivity");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки клавиатуры активности: {ex.Message}");
            }
        }

        private UserParameters CalculateFitnessPlan(UserState userState)
        {
            // Формула Миффлина-Сан Жеора
            double bmr = userState.Gender == "male"
                ? 10 * userState.Weight.Value + 6.25 * userState.Height.Value - 5 * userState.Age.Value + 5
                : 10 * userState.Weight.Value + 6.25 * userState.Height.Value - 5 * userState.Age.Value - 161;

            // Коэффициент активности
            double activityMultiplier = userState.ActivityLevel switch
            {
                "sedentary" => 1.2,
                "light" => 1.375,
                "moderate" => 1.55,
                "active" => 1.725,
                "very_active" => 1.9,
                _ => 1.2
            };

            double calories = bmr * activityMultiplier;

            // Корректировка по цели
            calories = userState.Goal switch
            {
                "weight_loss" => calories - 500,
                "weight_gain" => calories + 500,
                _ => calories
            };

            // Расчет БЖУ
            int protein = (int)(userState.Weight.Value * 2.2); // 2.2г белка на кг веса
            int fat = (int)(userState.Weight.Value * 1); // 1г жиров на кг веса
            int carbs = (int)((calories - (protein * 4 + fat * 9)) / 4); // Остальное - углеводы

            return new UserParameters
            {
                Weight = userState.Weight.Value,
                Height = userState.Height.Value,
                Age = userState.Age.Value,
                Gender = userState.Gender,
                Goal = userState.Goal,
                ActivityLevel = userState.ActivityLevel,
                DailyCalories = (int)calories,
                ProteinGoal = protein,
                FatGoal = fat,
                CarbsGoal = carbs,
                WorkoutPlan = GenerateWorkoutPlan(userState),
                DietAdvice = GenerateDietAdvice(userState.Goal)
            };
        }

        private string GenerateWorkoutPlan(UserState userState)
        {
            return userState.Goal switch
            {
                "weight_loss" => "🏃‍♂️ **Тренировки для похудения:**\n" +
                                "• 3-4 раза в неделю: кардио 30-45 минут\n" +
                                "• Силовые тренировки всего тела: 3 подхода по 12-15 повторений\n" +
                                "• Упражнения: приседания, выпады, отжимания, планка, берпи\n" +
                                "• Интервальные тренировки (HIIT) 2 раза в неделю\n" +
                                "• Общее время тренировки: 45-60 минут",

                "weight_gain" => "💪 **Тренировки для набора массы:**\n" +
                                "• 3 раза в неделю: сплит тренировки\n" +
                                "• Пн: Грудь/Трицепс\n" +
                                "• Ср: Спина/Бицепс\n" +
                                "• Пт: Ноги/Плечи\n" +
                                "• Силовые упражнения: 4 подхода по 8-12 повторений\n" +
                                "• База: жим лежа, становая тяга, приседания\n" +
                                "• Отдых между подходами: 60-90 секунд",

                _ => "⚖️ **Тренировки для поддержания формы:**\n" +
                     "• 3 раза в неделю: круговые тренировки всего тела\n" +
                     "• Сочетание кардио и силовых упражнений\n" +
                     "• 3 подхода по 10-12 повторений\n" +
                     "• Упражнения: приседания, отжимания, подтягивания, планка\n" +
                     "• Продолжительность: 40-50 минут за тренировку"
            };
        }

        private string GenerateDietAdvice(string goal)
        {
            return goal switch
            {
                "weight_loss" => "🥗 **Питание для похудения:**\n" +
                                "• Дефицит калорий: потребляйте на 500 ккал меньше нормы\n" +
                                "• Белки: 2-2.5г на кг веса (курица, рыба, тофу, творог)\n" +
                                "• Овощи: не менее 400г в день\n" +
                                "• Исключите: сахар, processed food, сладкие напитки\n" +
                                "• Пейте 2-3 литра воды в день\n" +
                                "• Пример приема пищи: куриная грудка 150г + гречка 100г + овощной салат\n" +
                                "• Последний прием пищи: за 3-4 часа до сна",

                "weight_gain" => "🍗 **Питание для набора массы:**\n" +
                                "• Профицит калорий: потребляйте на 500 ккал больше нормы\n" +
                                "• Белки: 2-2.5г на кг веса (говядина, курица, яйца, рыба)\n" +
                                "• Углеводы: сложные (гречка, рис, овсянка, макароны из твердых сортов)\n" +
                                "• 5-6 приемов пищи в день + перекусы\n" +
                                "• Перекусы: орехи, творог, протеиновые коктейли, бананы\n" +
                                "• Пример приема пищи: говядина 200г + рис 150г + овощи + авокадо",

                _ => "🥦 **Сбалансированное питание:**\n" +
                     "• Поддерживайте баланс БЖУ согласно расчетам\n" +
                     "• Белки: 1.5-2г на кг веса (курица, рыба, бобовые)\n" +
                     "• Жиры: 1г на кг веса (орехи, авокадо, оливковое масло, рыбий жир)\n" +
                     "• Углеводы: сложные (крупы, цельнозерновой хлеб, овощи)\n" +
                     "• Ешьте разнообразную пищу, 4-5 приемов в день\n" +
                     "• Не забывайте про фрукты и овощи (5 порций в день)"
            };
        }

        private async Task ProcessResultsAsync(ITelegramBotClient botClient, long chatId, UserState userState, CancellationToken cancellationToken)
        {
            try
            {
                Console.WriteLine("Начинаем расчет результатов...");

                // Расчет плана
                var parameters = CalculateFitnessPlan(userState);
                Console.WriteLine("План рассчитан");

                // Сохранение в БД
                var userId = await _dbService.GetOrCreateUserAsync(chatId, null, null, null);
                await _dbService.SaveUserParametersAsync(userId, parameters);
                Console.WriteLine("Данные сохранены в БД");

                // Формирование сообщения
                var resultMessage = $@"🎯 **Ваш персональный фитнес-план:**

📊 **Данные:**
• Пол: {(userState.Gender == "male" ? "Мужской" : "Женский")}
• Возраст: {userState.Age} лет
• Вес: {userState.Weight} кг
• Рост: {userState.Height} см
• Цель: {userState.Goal switch
                {
                    "weight_loss" => "Похудение",
                    "maintenance" => "Поддержание веса",
                    "weight_gain" => "Набор массы",
                    _ => "Не определена"
                }}
• Активность: {userState.ActivityLevel switch
                {
                    "sedentary" => "Сидячий",
                    "light" => "Легкая",
                    "moderate" => "Умеренная",
                    "active" => "Высокая",
                    "very_active" => "Очень высокая",
                    _ => "Не определена"
                }}

🍽 **Питание:**
• Калории: {parameters.DailyCalories} ккал/день
• Белки: {parameters.ProteinGoal} г/день
• Жиры: {parameters.FatGoal} г/день
• Углеводы: {parameters.CarbsGoal} г/день

{parameters.WorkoutPlan}

{parameters.DietAdvice}

📝 **Рекомендации:**
• Взвешивайтесь 1 раз в неделю утром натощак
• Пейте достаточное количество воды (30-40 мл на кг веса)
• Спите 7-8 часов в сутки
• Делайте прогрессию нагрузок
• Ведите дневник питания и тренировок

💡 **Совет:** Начинайте постепенно, не пытайтесь сразу выполнить всю программу.

Для нового расчета напишите /start";

                // Отправка сообщения
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: resultMessage,
                    cancellationToken: cancellationToken);

                Console.WriteLine("Сообщение отправлено пользователю");


                // Сброс состояния
                userState.Reset();
                Console.WriteLine("Состояние пользователя сброшено");

                await ShowMainMenu(botClient, chatId, cancellationToken);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в ProcessResultsAsync: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");

                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Произошла ошибка при расчете плана. Попробуйте позже или напишите /start для перезапуска.",
                    cancellationToken: cancellationToken);
            }
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiRequestException =>
                    $"Telegram API Error: [{apiRequestException.ErrorCode}] {apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.WriteLine($"Ошибка polling: {errorMessage}");
            return Task.CompletedTask;
        }

        private async Task HandleStartCommand(ITelegramBotClient botClient, long chatId, UserState userState, CancellationToken cancellationToken)
        {
            userState.Reset();

            // Проверяем есть ли у пользователя профиль
            var hasProfile = await _dbService.UserHasProfileAsync(chatId);

            if (hasProfile)
            {
                await ShowMainMenu(botClient, chatId, cancellationToken);
            }
            else
            {
                await SendGenderKeyboardAsync(botClient, chatId, cancellationToken);
            }
        }

        private async Task ShowMainMenu(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            var keyboard = new ReplyKeyboardMarkup(new[]
    {
        new[] { new KeyboardButton("📊 Мой профиль"), new KeyboardButton("⚖️ Обновить вес") },
        new[] { new KeyboardButton("🎯 Новый план"), new KeyboardButton("📋 Список упражнений") },
        new[] { new KeyboardButton("❌ Отмена") }
    })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            };

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Главное меню. Выберите действие:",
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);

            if (_userStates.TryGetValue(chatId, out var userState))
            {
                userState.CurrentState = ConversationState.MainMenu;
            }
        }

        private async Task ShowUserProfile(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            try
            {
                Console.WriteLine($"Показать профиль для chatId: {chatId}");

                var parameters = await _dbService.GetLatestUserParametersAsync(chatId);

                if (parameters == null)
                {
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Профиль не найден. Создайте новый план с помощью /start",
                        cancellationToken: cancellationToken);
                    return;
                }

                // УБРАТЬ ParseMode.Markdown и использовать простой текст
                var profileMessage = $@"📊 ВАШ ПРОФИЛЬ:

• Пол: {(parameters.Gender == "male" ? "Мужской" : "Женский")}
• Возраст: {parameters.Age} лет
• Вес: {parameters.Weight} кг
• Рост: {parameters.Height} см
• Цель: {parameters.Goal switch
                {
                    "weight_loss" => "Похудение",
                    "maintenance" => "Поддержание веса",
                    "weight_gain" => "Набор массы",
                    _ => "Не определена"
                }}
• Активность: {parameters.ActivityLevel switch
                {
                    "sedentary" => "Сидячий",
                    "light" => "Легкая",
                    "moderate" => "Умеренная",
                    "active" => "Высокая",
                    "very_active" => "Очень высокая",
                    _ => "Не определена"
                }}

🍽 ПИТАНИЕ:
• Калории: {parameters.DailyCalories} ккал/день
• Белки: {parameters.ProteinGoal} г/день
• Жиры: {parameters.FatGoal} г/день
• Углеводы: {parameters.CarbsGoal} г/день

Используйте /update_weight чтобы обновить вес";

                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: profileMessage,
                    // УБРАТЬ parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);

                // После показа профиля возвращаем в главное меню
                await ShowMainMenu(botClient, chatId, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в ShowUserProfile: {ex.Message}");
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Ошибка при загрузке профиля. Попробуйте позже.",
                    cancellationToken: cancellationToken);
            }
        }

        private async Task StartWeightUpdate(ITelegramBotClient botClient, long chatId, UserState userState, CancellationToken cancellationToken)
        {
            // Загружаем существующий профиль
            var parameters = await _dbService.GetLatestUserParametersAsync(chatId);

            if (parameters == null)
            {
                await botClient.SendTextMessageAsync(chatId, "Сначала создайте профиль через /start", cancellationToken: cancellationToken);
                return;
            }

            // Загружаем данные в состояние
            userState.LoadFromProfile(parameters);
            userState.IsUpdatingWeight = true;
            userState.CurrentState = ConversationState.UpdatingWeight;

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"Текущий вес: {parameters.Weight} кг\nВведите новый вес:",
                cancellationToken: cancellationToken);
        }

        private async Task ShowExercisesMenu(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            var keyboard = new ReplyKeyboardMarkup(new[]
            {
        new[] { new KeyboardButton("🏃‍♂️ Упражнения для похудения") },
        new[] { new KeyboardButton("💪 Упражнения для набора массы") },
        new[] { new KeyboardButton("⚖️ Упражнения для поддержания") },
        new[] { new KeyboardButton("↩️ Назад в меню") }
    })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            };

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Выберите цель для просмотра упражнений:",
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);

            if (_userStates.TryGetValue(chatId, out var userState))
            {
                userState.CurrentState = ConversationState.ShowingExercisesMenu;
            }
        }
        private async Task ShowWeightLossExercises(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            var exercises = @"
🔥 УПРАЖНЕНИЯ ДЛЯ ПОХУДЕНИЯ 🔥

🏋️‍♂️ БАЗОВЫЕ УПРАЖНЕНИЯ:
• Приседания со штангой - 3×12-15
• Жим лежа - 3×12-15
• Становая тяга - 3×12-15
• Тяга верхнего блока - 3×12-15
• Жим ногами - 3×15-20

⚡ КАРДИО УПРАЖНЕНИЯ:
• Бег интервальный - 25-30 мин
• Велотренажер - 30-40 мин
• Эллипс - 30 мин
• Скакалка - 10-15 мин

💥 HIIT УПРАЖНЕНИЯ:
• Берпи - 45 сек работа/15 отдых
• Альпинист - 45 сек/15 отдых
• Прыжки с приседом - 40 сек/20 отдых

📋 ФОРМАТ ТРЕНИРОВКИ:
• Разминка: 10-15 мин
• Силовая часть: 45-50 мин
• Кардио: 25-30 мин
• Заминка: 10-15 мин

💡 СОВЕТ: Делайте 3-4 тренировки в неделю, сочетая силовые и кардио и не забывайте про дефецит каллорий";

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: exercises,
                cancellationToken: cancellationToken);

            await ShowExercisesMenu(botClient, chatId, cancellationToken);
        }

        private async Task ShowMassGainExercises(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            var exercises = @"
💪 УПРАЖНЕНИЯ ДЛЯ НАБОРА МАССЫ 💪

🏋️‍♂️ БАЗА (ОСНОВА РОСТА):
• Понедельник Спина/Бицепс
    1. Подтягивания на перекладине - 4х6-8
    2. Румынская тяга - 4х6-8
    3. Тяга штанги к поясу - 4х6-8
    4. Подъём зет-штанги - 4х10-12
• Среда Грудь/Трицепс
    1. Жим лёжа 4х6-8
    2. Жим гантелей на наклонной скамье - 4х6-8
    3. Сведения в кроссовере - 4х12-20
    4. Французский жим - 4х10-12
• Пятница Ноги/Плечи
    1. Приседания со штангой - 4х6-8
    2. Жим ногами - 4х8-12
    3. Жим на икры - 4х12-20
    4. Махи руками с гантелями - 4х10-12

📈 ВСПОМОГАТЕЛЬНЫЕ:
• Разводка гантелей - 3×15
• Вертикальная тяга - 3x12
• Жим к низу в блочном тренажёре - 3x12
• Разгибания ног - 3×12-15
• Сгибания ног - 3×12-15
• Подъемы на носки - 4×15-20

📋 ФОРМАТ ТРЕНИРОВКИ:
• Разминка: 10-15 мин
• Основные упражнения: 60-70 мин
• Вспомогательные: 20-30 мин
• Растяжка: 10 мин

💡 СОВЕТ: Соблюдайте прогрессию весов в упражнениях (1-ый подход 50% от максимального, 4-ый подход 80-85% от максимального веса)!";

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: exercises,
                cancellationToken: cancellationToken);

            await ShowExercisesMenu(botClient, chatId, cancellationToken);
        }

        private async Task ShowMaintenanceExercises(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            var exercises = @"
⚖️ УПРАЖНЕНИЯ ДЛЯ ПОДДЕРЖАНИЯ ФОРМЫ ⚖️

🏋️‍♂️ КРУГОВАЯ ТРЕНИРОВКА:
• Приседания - 3×12-15
• Отжимания - 3×12-15
• Тяга гантели - 3×12 на сторону
• Планка - 3×60 сек
• Выпады - 3×12 на ногу

🎯 ФУНКЦИОНАЛЬНЫЕ:
• Берпи - 3×10
• Прыжки на скакалке - 3×100
• Боковая планка - 3×45 сек
• Подъемы корпуса - 3×20
• Ягодичный мостик - 3×15

🏃‍♂️ КАРДИО МИКС:
• Бег трусцой - 20-30 мин
• Велосипед - 25-35 мин
• Плавание - 30-40 мин
• Скандинавская ходьба - 40-50 мин

📋 ФОРМАТ ТРЕНИРОВКИ:
• Разминка: 10 мин
• Основной блок: 45-50 мин
• Кардио: 20-25 мин
• Растяжка: 10-15 мин

💡 СОВЕТ: 3 тренировки в неделю + активный отдых";

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: exercises,
                cancellationToken: cancellationToken);

            await ShowExercisesMenu(botClient, chatId, cancellationToken);
        }
    }
}
