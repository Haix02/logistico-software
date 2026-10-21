# Logistico web host (REST API + Blazor WebAssembly UI) as a container image.
# CI publishes the official image with .NET's built-in container support (see .github/workflows/ci.yml);
# this Dockerfile is for `docker compose up` and for building the image without the .NET SDK installed.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so package downloads are cached between source changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Logistico.SharedKernel/Logistico.SharedKernel.csproj src/Logistico.SharedKernel/
COPY src/Logistico.Web/Logistico.Web.csproj src/Logistico.Web/
COPY src/Logistico.Web.Client/Logistico.Web.Client.csproj src/Logistico.Web.Client/
COPY src/Modules/Merchants/Logistico.Modules.Merchants/Logistico.Modules.Merchants.csproj src/Modules/Merchants/Logistico.Modules.Merchants/
COPY src/Modules/Merchants/Logistico.Modules.Merchants.Contracts/Logistico.Modules.Merchants.Contracts.csproj src/Modules/Merchants/Logistico.Modules.Merchants.Contracts/
COPY src/Modules/Shipments/Logistico.Modules.Shipments/Logistico.Modules.Shipments.csproj src/Modules/Shipments/Logistico.Modules.Shipments/
COPY src/Modules/Shipments/Logistico.Modules.Shipments.Contracts/Logistico.Modules.Shipments.Contracts.csproj src/Modules/Shipments/Logistico.Modules.Shipments.Contracts/
RUN dotnet restore src/Logistico.Web/Logistico.Web.csproj

COPY src/ src/
# Publish restores again on purpose: the Web SDK adds the package that serves _framework/blazor.web.js only when it
# sees the .razor files, which the csproj-only restore above cannot. Packages are already cached, so this is quick.
RUN dotnet publish src/Logistico.Web/Logistico.Web.csproj -c Release -o /app \
    && mkdir -p /keys

# Small, shell-less Ubuntu runtime with ICU (Arabic culture) and time-zone data; runs as the non-root "app" user.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final
WORKDIR /app
COPY --from=build --chown=1654:1654 /app .
# Data-protection keys (sign-in cookies) live here; mount a volume so sign-ins survive restarts.
COPY --from=build --chown=1654:1654 /keys /keys
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__KeyRingPath=/keys
EXPOSE 8080
USER 1654
ENTRYPOINT ["dotnet", "Logistico.Web.dll"]
