FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first for better layer caching
COPY Directory.Build.props .
COPY src/DeepMind.Core/DeepMind.Core.csproj src/DeepMind.Core/
COPY src/DeepMind.Server/DeepMind.Server.csproj src/DeepMind.Server/
RUN dotnet restore src/DeepMind.Server/DeepMind.Server.csproj

# Copy source and publish
COPY src/ src/
RUN dotnet publish src/DeepMind.Server/DeepMind.Server.csproj \
    -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN mkdir -p /data/deepmind /data/deepmind/backups /data/deepmind/logs

COPY --from=build /app/publish .

ENV DEEPMIND_TRANSPORT=http
ENV ASPNETCORE_URLS=http://0.0.0.0:6868
ENV DeepMind__Database__Path=/data/deepmind/deepmind.db
ENV DeepMind__Backup__Path=/data/deepmind/backups
ENV DeepMind__Logging__FilePath=/data/deepmind/logs/deepmind.log

EXPOSE 6868

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:6868/health || exit 1

ENTRYPOINT ["dotnet", "DeepMind.Server.dll"]
