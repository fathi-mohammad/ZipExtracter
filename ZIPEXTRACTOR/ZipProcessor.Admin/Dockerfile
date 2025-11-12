# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ZipProcessor.Admin.csproj", "."]
RUN dotnet restore "./ZipProcessor.Admin.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "./ZipProcessor.Admin.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ZipProcessor.Admin.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ZipProcessor.Admin.dll"]


## build image
#docker build -t zipprocessor.admin:latest .
#
## run container (map host port 80 to container 8080)
#docker run -d \
  #--name zipprocessor \
  #-p 80:8080 \
  #-e ASPNETCORE_ENVIRONMENT=Production \
  #-e DOTNET_PRINT_TELEMETRY_MESSAGE=false \
  #zipprocessor.admin:latest
#
## view logs
#docker logs -f zipprocessor

 #Set-Location "D:\WORK\ZIPEXTRACTOR\ZipProcessor.Admin\bin\Release\net9.0\publish"; $env:ASPNETCORE_ENVIRONMENT="Production"; $env:ASPNETCORE_URLS="http://+:8080"; dotnet .\ZipProcessor.Admin.dll

#docker run -d --name zipprocessor -p 80:8080 -e ASPNETCORE_ENVIRONMENT=Production -e DOTNET_PRINT_TELEMETRY_MESSAGE=false  zipprocessor.admin:latest