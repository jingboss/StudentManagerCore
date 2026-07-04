# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore (caching layer)
COPY ["StudentManagerCore.csproj", "."]
RUN dotnet restore

# Copy everything else and publish
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install locales for Chinese charset support
RUN apt-get update && \
    apt-get install -y --no-install-recommends locales && \
    sed -i '/zh_CN.UTF-8/s/^# //' /etc/locale.gen && \
    locale-gen && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

ENV LANG=zh_CN.UTF-8 \
    LC_ALL=zh_CN.UTF-8 \
    ASPNETCORE_URLS=http://+:5000 \
    ASPNETCORE_ENVIRONMENT=Production

# Copy published app
COPY --from=build /app/publish .

# Create directories for uploads
RUN mkdir -p uploads/imge uploads/survey

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:5000/Home/Index || exit 1

EXPOSE 5000

ENTRYPOINT ["dotnet", "StudentManagerCore.dll"]
