# ========================================
# 1️⃣ BUILD STAGE – build and publish app
# ========================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["ZipProcessor.Worker.csproj", "./"]
RUN dotnet restore "ZipProcessor.Worker.csproj"

# Copy everything and publish (release build)
COPY . .
RUN dotnet publish "ZipProcessor.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ========================================
# 2️⃣ RUNTIME STAGE – minimal runtime image
# ========================================
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

# Optional environment tuning
ENV DOTNET_EnableDiagnostics=0 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_GCServer=1 \
    DOTNET_RUNNING_IN_CONTAINER=true

ENTRYPOINT ["dotnet", "ZipProcessor.Worker.dll"]
