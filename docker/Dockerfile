FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project files and restore
COPY src/Freebuff.Platform.Domain/Freebuff.Platform.Domain.csproj Freebuff.Platform.Domain/
COPY src/Freebuff.Platform.Shared/Freebuff.Platform.Shared.csproj Freebuff.Platform.Shared/
COPY src/Freebuff.Platform.Application/Freebuff.Platform.Application.csproj Freebuff.Platform.Application/
COPY src/Freebuff.Platform.Infrastructure/Freebuff.Platform.Infrastructure.csproj Freebuff.Platform.Infrastructure/
COPY src/Freebuff.Platform.Api/Freebuff.Platform.Api.csproj Freebuff.Platform.Api/
RUN dotnet restore Freebuff.Platform.Api/Freebuff.Platform.Api.csproj

# Copy everything and build
COPY src/ .
RUN dotnet build Freebuff.Platform.Api/Freebuff.Platform.Api.csproj -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish Freebuff.Platform.Api/Freebuff.Platform.Api.csproj -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Freebuff.Platform.Api.dll"]
