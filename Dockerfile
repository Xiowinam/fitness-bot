FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Копируем файл проекта из ВЛОЖЕННОЙ папки FitnessBot/
COPY ["FitnessBot/FitnessBot.csproj", "FitnessBot/"]
RUN dotnet restore "FitnessBot/FitnessBot.csproj"

# Копируем весь исходный код
COPY . .
RUN dotnet publish "FitnessBot/FitnessBot.csproj" -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS="http://*:${PORT:-8080}"
EXPOSE ${PORT:-8080}

ENTRYPOINT ["dotnet", "FitnessBot.dll"]