# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore PCTimeLimitServer/PCTimeLimitServer.csproj
RUN dotnet publish PCTimeLimitServer/PCTimeLimitServer.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:5005
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

EXPOSE 5005
ENTRYPOINT ["dotnet", "PCTimeLimitServer.dll"]
