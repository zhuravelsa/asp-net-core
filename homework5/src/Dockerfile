# Контекст сборки — корень репозитория (homework5), см. docker-compose.yaml

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Сначала только файлы проектов — чтобы restore кэшировался отдельным слоем
COPY src/PromoCodeFactory.Core/PromoCodeFactory.Core.csproj PromoCodeFactory.Core/
COPY src/PromoCodeFactory.DataAccess/PromoCodeFactory.DataAccess.csproj PromoCodeFactory.DataAccess/
COPY src/PromoCodeFactory.WebHost/PromoCodeFactory.WebHost.csproj PromoCodeFactory.WebHost/
RUN dotnet restore PromoCodeFactory.WebHost/PromoCodeFactory.WebHost.csproj

# Исходники и сборка
COPY src/PromoCodeFactory.Core/ PromoCodeFactory.Core/
COPY src/PromoCodeFactory.DataAccess/ PromoCodeFactory.DataAccess/
COPY src/PromoCodeFactory.WebHost/ PromoCodeFactory.WebHost/
RUN dotnet build PromoCodeFactory.WebHost/PromoCodeFactory.WebHost.csproj \
    -c Release --no-restore -o /app/build

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/build .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "PromoCodeFactory.WebHost.dll"]
