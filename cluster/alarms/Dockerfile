FROM microsoft/dotnet:2.0-runtime AS base
WORKDIR /app

FROM microsoft/dotnet:2.0-sdk AS build
WORKDIR /src
COPY ["alarms.csproj", "./"]
RUN dotnet restore "./alarms.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "alarms.csproj" -c Release -o /app

FROM build AS publish
RUN dotnet publish "alarms.csproj" -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "alarms.dll"]
