FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first for better layer caching
COPY Directory.Build.props .
COPY src/MemoryBank.Core/MemoryBank.Core.csproj src/MemoryBank.Core/
COPY src/MemoryBank.Server/MemoryBank.Server.csproj src/MemoryBank.Server/
RUN dotnet restore src/MemoryBank.Server/MemoryBank.Server.csproj

# Copy source and publish
COPY src/ src/
RUN dotnet publish src/MemoryBank.Server/MemoryBank.Server.csproj \
    -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN mkdir -p /data/memorybank /data/memorybank/backups /data/memorybank/logs

COPY --from=build /app/publish .

ENV MEMORYBANK_TRANSPORT=http
ENV ASPNETCORE_URLS=http://0.0.0.0:6868
ENV MemoryBank__Database__Path=/data/memorybank/memorybank.db
ENV MemoryBank__Backup__Path=/data/memorybank/backups
ENV MemoryBank__Logging__FilePath=/data/memorybank/logs/memorybank.log

EXPOSE 6868

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:6868/health || exit 1

ENTRYPOINT ["dotnet", "MemoryBank.Server.dll"]
