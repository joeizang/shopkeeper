FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend-api/src/Shopkeeper.Api/ ./Shopkeeper.Api/
WORKDIR /src/Shopkeeper.Api
RUN dotnet restore Shopkeeper.Api.csproj
RUN dotnet build Shopkeeper.Api.csproj -c Release --no-restore
RUN dotnet publish Shopkeeper.Api.csproj -c Release -o /app/publish --no-build /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:5000
EXPOSE 5000
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "Shopkeeper.Api.dll"]
