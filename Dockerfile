# AeroDriver Dockerfile for different components

# Base image for .NET 8 applications
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["AeroDriver.sln", "."]
COPY ["src/AeroDriver.Core/AeroDriver.Core.csproj", "src/AeroDriver.Core/"]
COPY ["src/AeroDriver.API/AeroDriver.API.csproj", "src/AeroDriver.API/"]
COPY ["src/AeroDriver.Service/AeroDriver.Service.csproj", "src/AeroDriver.Service/"]
COPY ["src/AeroDriver.UI/AeroDriver.UI.csproj", "src/AeroDriver.UI/"]
COPY ["src/AeroDriver.CLI/AeroDriver.CLI.csproj", "src/AeroDriver.CLI/"]
COPY ["src/AeroDriver.Tests/AeroDriver.Tests.csproj", "src/AeroDriver.Tests/"]

# Restore dependencies
RUN dotnet restore "AeroDriver.sln"

# Copy source code
COPY ["src/", "src/"]

# Build API
FROM build AS build-api
WORKDIR "/src/src/AeroDriver.API"
RUN dotnet build "AeroDriver.API.csproj" -c Release -o /app/build

FROM build-api AS publish-api
RUN dotnet publish "AeroDriver.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Build Service
FROM build AS build-service
WORKDIR "/src/src/AeroDriver.Service"
RUN dotnet build "AeroDriver.Service.csproj" -c Release -o /app/build

FROM build-service AS publish-service
RUN dotnet publish "AeroDriver.Service.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Build CLI
FROM build AS build-cli
WORKDIR "/src/src/AeroDriver.CLI"
RUN dotnet build "AeroDriver.CLI.csproj" -c Release -o /app/build

FROM build-cli AS publish-cli
RUN dotnet publish "AeroDriver.CLI.csproj" -c Release -o /app/publish

# Runtime images

# API Runtime
FROM base AS api-runtime
WORKDIR /app
COPY --from=publish-api /app/publish .
ENTRYPOINT ["dotnet", "AeroDriver.API.dll"]

# Service Runtime
FROM base AS service-runtime
WORKDIR /app
COPY --from=publish-service /app/publish .
ENTRYPOINT ["dotnet", "AeroDriver.Service.dll"]

# CLI Runtime
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS cli-runtime
WORKDIR /app
COPY --from=publish-cli /app/publish .
ENTRYPOINT ["dotnet", "AeroDriver.CLI.dll"]
