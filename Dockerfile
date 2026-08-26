FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/WeatherTrackerLite.Web/WeatherTrackerLite.Web.csproj src/WeatherTrackerLite.Web/
RUN dotnet restore src/WeatherTrackerLite.Web/WeatherTrackerLite.Web.csproj

COPY src/WeatherTrackerLite.Web/ src/WeatherTrackerLite.Web/
RUN dotnet publish src/WeatherTrackerLite.Web/WeatherTrackerLite.Web.csproj --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "WeatherTrackerLite.Web.dll"]
