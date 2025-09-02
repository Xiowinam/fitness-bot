# Используем образ SDK для сборки
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Копируем файл проекта и восстанавливаем зависимости
COPY ["FitnessBot.csproj", "."]
RUN dotnet restore "FitnessBot.csproj"

# Копируем весь исходный код и собираем приложение
COPY . .
RUN dotnet publish -c Release -o /app

# Используем образ рантайма для запуска (он меньше)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Копируем собранное приложение из стадии build
COPY --from=build /app .

# Говорим .NET слушать порт, который предоставит Render
# Для локальной проверки по умолчанию используется порт 8080
ENV ASPNETCORE_URLS="http://*:8080"
EXPOSE 8080

# Запускаем DLL-файл
ENTRYPOINT ["dotnet", "FitnessBot.dll"]