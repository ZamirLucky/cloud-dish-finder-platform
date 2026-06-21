# Runtime base image used by Visual Studio fast mode and final production container.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app

# Cloud Run and .NET 8 containers use port 8080.
ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080


# Build stage: restores and builds the ASP.NET Core MVC project.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["DishFinder/DishFinder.csproj", "DishFinder/"]
RUN dotnet restore "./DishFinder/DishFinder.csproj"

COPY . .
WORKDIR "/src/DishFinder"
RUN dotnet build "./DishFinder.csproj" -c $BUILD_CONFIGURATION -o /app/build


# Publish stage: creates the final published application files.
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./DishFinder.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false


# Final stage: runs only the published output.
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "DishFinder.dll"]