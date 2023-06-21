FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["RtmpServer/RtmpServer.csproj", "RtmpServer/"]
RUN dotnet restore "RtmpServer/RtmpServer.csproj"
COPY . .
WORKDIR "/src/RtmpServer"
RUN dotnet build "RtmpServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "RtmpServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RtmpServer.dll"]
