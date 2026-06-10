# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution + project files first for better layer caching
COPY StrategyHouse.sln ./
COPY StrategyHouse.Domain/StrategyHouse.Domain.csproj StrategyHouse.Domain/
COPY StrategyHouse.Infrastructure/StrategyHouse.Infrastructure.csproj StrategyHouse.Infrastructure/
COPY StrategyHouse.Web/StrategyHouse.Web.csproj StrategyHouse.Web/
RUN dotnet restore StrategyHouse.sln

# Copy the rest of the source and publish
COPY . .
RUN dotnet publish StrategyHouse.Web/StrategyHouse.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Railway/Render/Fly inject PORT — bind to it; default 8080 locally.
ENV ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080}
EXPOSE 8080

ENTRYPOINT ["dotnet", "StrategyHouse.Web.dll"]
