# StudentManagerCore - Dockerfile
# 构建阶段
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 复制项目文件并还原
COPY StudentManagerCore.csproj .
RUN dotnet restore

# 复制全部源码并发布
COPY . .
RUN dotnet publish -c Release -o /app/publish

# 运行时阶段
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
EXPOSE 80
EXPOSE 443

# 安装中文语言包（支持中文排序等）
RUN apt-get update && apt-get install -y locales && \
    sed -i 's/^# *zh_CN.UTF-8/zh_CN.UTF-8/' /etc/locale.gen && \
    locale-gen && \
    apt-get clean && rm -rf /var/lib/apt/lists/*

ENV LANG=zh_CN.UTF-8 \
    LC_ALL=zh_CN.UTF-8 \
    ASPNETCORE_URLS=http://+:80 \
    ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "StudentManagerCore.dll"]
