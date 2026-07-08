FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project files
COPY ["src/SampleApp/SampleApp.csproj", "src/SampleApp/"]
COPY ["src/Instrumentation/LogMonitoring.Instrumentation.csproj", "src/Instrumentation/"]
RUN dotnet restore "src/SampleApp/SampleApp.csproj"

# Copy source and build
COPY . .
WORKDIR "/src/src/SampleApp"
RUN dotnet build "SampleApp.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "SampleApp.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317

HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "SampleApp.dll"]
