# 构建阶段使用与 global.json 对齐的稳定 .NET 10 SDK。
FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /source
COPY global.json Directory.Build.props NuGet.Config ./
COPY src ./src
RUN dotnet publish src/AI.Customer.Service.Web -c Release -o /app --no-self-contained

# 独立运行目录保存客服 SQLite 数据；生产可配置 MySQL 或 Kingbase。
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
USER root
RUN mkdir -p /app/data/runtime && chown -R $APP_UID /app/data
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
VOLUME /app/data/runtime
ENTRYPOINT ["dotnet", "AI.Customer.Service.dll"]
