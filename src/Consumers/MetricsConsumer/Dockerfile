FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project file and restore
COPY ["src/Consumers/MetricsConsumer/MetricsConsumer.csproj", "src/Consumers/MetricsConsumer/"]
RUN dotnet restore "src/Consumers/MetricsConsumer/MetricsConsumer.csproj"

# Copy source and build
COPY . .
WORKDIR "/src/src/Consumers/MetricsConsumer"
RUN dotnet build "MetricsConsumer.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "MetricsConsumer.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Add healthcheck
HEALTHCHECK --interval=30s --timeout=10s --start-period=10s --retries=3 \
    CMD pgrep -f dotnet || exit 1

ENTRYPOINT ["dotnet", "MetricsConsumer.dll"]
