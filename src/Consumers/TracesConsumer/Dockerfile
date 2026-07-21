FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project file and restore
COPY ["src/Consumers/TracesConsumer/TracesConsumer.csproj", "src/Consumers/TracesConsumer/"]
COPY ["src/OtlpProto/OtlpProto.csproj", "src/OtlpProto/"]
RUN dotnet restore "src/Consumers/TracesConsumer/TracesConsumer.csproj"

# Copy source and build
COPY . .
WORKDIR "/src/src/Consumers/TracesConsumer"
RUN dotnet build "TracesConsumer.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "TracesConsumer.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .


ENTRYPOINT ["dotnet", "TracesConsumer.dll"]
