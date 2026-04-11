# syntax=docker/dockerfile:1.7
#
# IntelliTrader production image.
#
# NOTE: the original issue (#2) suggested .NET 8.0 base images, but the
# codebase has since been migrated to .NET 9 (every csproj targets net9.0).
# We use the .NET 9 SDK/runtime images instead, which is the only build
# that actually compiles against the current sources.
#
# Size budget: < 200 MB for the final image. Using the Alpine aspnet base
# image keeps us well under that while still providing /bin/sh and wget for
# the HEALTHCHECK step.

ARG DOTNET_VERSION=9.0
ARG APP_UID=1654

########################
# Stage 1: restore
########################
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-alpine AS restore
WORKDIR /src

# Copy csproj files first to leverage Docker layer caching: this layer is
# only invalidated when a csproj changes. Test projects are intentionally
# excluded — `dotnet restore IntelliTrader/IntelliTrader.csproj` walks
# ProjectReference recursively from the entry-point csproj and never
# touches the .sln, so test projects do not need to be in the build
# context (they are excluded via .dockerignore).
COPY IntelliTrader/IntelliTrader.csproj                           IntelliTrader/
COPY IntelliTrader.Core/IntelliTrader.Core.csproj                 IntelliTrader.Core/
COPY IntelliTrader.Domain/IntelliTrader.Domain.csproj             IntelliTrader.Domain/
COPY IntelliTrader.Application/IntelliTrader.Application.csproj   IntelliTrader.Application/
COPY IntelliTrader.Infrastructure/IntelliTrader.Infrastructure.csproj IntelliTrader.Infrastructure/
COPY IntelliTrader.Trading/IntelliTrader.Trading.csproj           IntelliTrader.Trading/
COPY IntelliTrader.Rules/IntelliTrader.Rules.csproj               IntelliTrader.Rules/
COPY IntelliTrader.Signals.Base/IntelliTrader.Signals.Base.csproj IntelliTrader.Signals.Base/
COPY IntelliTrader.Signals.TradingView/IntelliTrader.Signals.TradingView.csproj IntelliTrader.Signals.TradingView/
COPY IntelliTrader.Exchange.Base/IntelliTrader.Exchange.Base.csproj IntelliTrader.Exchange.Base/
COPY IntelliTrader.Exchange.Binance/IntelliTrader.Exchange.Binance.csproj IntelliTrader.Exchange.Binance/
COPY IntelliTrader.Integration.Base/IntelliTrader.Integration.Base.csproj IntelliTrader.Integration.Base/
COPY IntelliTrader.Integration.ProfitTrailer/IntelliTrader.Integration.ProfitTrailer.csproj IntelliTrader.Integration.ProfitTrailer/
COPY IntelliTrader.Backtesting/IntelliTrader.Backtesting.csproj   IntelliTrader.Backtesting/
COPY IntelliTrader.Web/IntelliTrader.Web.csproj                   IntelliTrader.Web/

RUN dotnet restore IntelliTrader/IntelliTrader.csproj

########################
# Stage 2: build + publish
########################
FROM restore AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy the full source tree now that restore has been cached.
COPY . .

RUN dotnet publish IntelliTrader/IntelliTrader.csproj \
      --configuration ${BUILD_CONFIGURATION} \
      --no-restore \
      --self-contained false \
      --output /app/publish \
      /p:UseAppHost=false

########################
# Stage 3: runtime
########################
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-alpine AS runtime

# Only wget is installed at runtime — it is needed by HEALTHCHECK to probe
# /api/health. Two intentional omissions to keep the final image under
# 200 MB:
#   * icu-libs: we run with invariant globalization (see ENV) because the
#     bot only parses JSON numbers, not localized strings.
#   * tzdata: the codebase has no TimeZoneInfo / FindSystemTimeZoneById
#     calls; the only timezone setting is a numeric TimezoneOffset in
#     core.json, which does not require IANA zone data.
RUN apk add --no-cache wget \
 && adduser -S -u ${APP_UID:-1654} -G root intellitrader || true

WORKDIR /app

# Copy published output and the default configuration templates in a
# single layer (chown is applied at COPY time, no separate chmod needed).
# Config is kept inside the image as a baseline; operators can mount a
# volume on /app/config to override individual files at runtime.
COPY --from=build --chown=${APP_UID}:0 /app/publish/ ./
COPY --from=build --chown=${APP_UID}:0 /src/IntelliTrader/config/ ./config/

# Pre-create data and log directories and hand them to the non-root user
# so mounted volumes with default ownership work out of the box.
RUN mkdir -p /app/data /app/log \
 && chown ${APP_UID}:0 /app/data /app/log \
 && chmod g=u /app /app/data /app/log

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:7000 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true

EXPOSE 7000

# Volumes for runtime-mutable state. Config is also a volume so operators
# can override the baked-in templates without rebuilding the image.
VOLUME ["/app/config", "/app/data", "/app/log"]

# Anonymous liveness endpoint defined in
# IntelliTrader.Web/MinimalApiEndpoints.cs — returns 200 OK as long as the
# web host is running. Kept outside of the authenticated /api group on
# purpose so container orchestrators can reach it without credentials.
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://127.0.0.1:7000/api/health || exit 1

USER ${APP_UID}

ENTRYPOINT ["dotnet", "IntelliTrader.dll"]
