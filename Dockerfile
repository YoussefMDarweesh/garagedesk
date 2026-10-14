# syntax=docker/dockerfile:1
#
# GarageDesk web app, built for Render (or any Docker host).
# The image contains no secrets: the database connection string only arrives at run time,
# as the environment variable ConnectionStrings__Default.

# ---- Build stage: the full SDK, thrown away after publishing ---------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project file alone. Docker caches this layer, so packages are only
# downloaded again when GarageDesk.Web.csproj changes, not on every code change.
COPY src/GarageDesk.Web/GarageDesk.Web.csproj src/GarageDesk.Web/
RUN dotnet restore src/GarageDesk.Web/GarageDesk.Web.csproj

COPY src/GarageDesk.Web/ src/GarageDesk.Web/
RUN dotnet publish src/GarageDesk.Web/GarageDesk.Web.csproj \
    --configuration Release --no-restore --output /app/publish

# ---- Run stage: only the ASP.NET runtime and the published app -------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Render terminates HTTPS and forwards plain HTTP; this makes the app trust X-Forwarded-Proto,
# so it knows the visitor is on HTTPS (secure cookies, correct links).
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
EXPOSE 8080

# The official image ships a non-root user ("app"); never run the website as root.
USER $APP_UID

# Render sets PORT at run time (and we listen on it); anywhere else it falls back to 8080.
# "exec" makes dotnet the main process, so it receives the stop signal and shuts down cleanly.
ENTRYPOINT ["sh", "-c", "export ASPNETCORE_URLS=\"http://+:${PORT:-8080}\"; exec dotnet GarageDesk.Web.dll"]
