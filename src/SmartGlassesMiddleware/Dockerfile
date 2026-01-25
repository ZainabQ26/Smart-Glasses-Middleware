FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# copy csproj and restore as distinct layers
COPY ["src/SmartGlassesMiddleware/SmartGlassesMiddleware.csproj", "src/SmartGlassesMiddleware/"]
RUN dotnet restore "src/SmartGlassesMiddleware/SmartGlassesMiddleware.csproj"

# copy everything else and publish
COPY . .
RUN dotnet publish "src/SmartGlassesMiddleware/SmartGlassesMiddleware.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "SmartGlassesMiddleware.dll"]
