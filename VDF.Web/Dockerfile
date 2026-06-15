# Stage 1: Build React SPA
FROM node:20-alpine AS frontend
WORKDIR /client
COPY VDF.Web.Client/package.json VDF.Web.Client/package-lock.json ./
RUN npm ci
COPY VDF.Web.Client/ .
RUN npm run build
# Output: /client/dist/

# Stage 2: Build .NET backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY ["VDF.Core/VDF.Core.csproj", "VDF.Core/"]
COPY ["VDF.Web/VDF.Web.csproj", "VDF.Web/"]
RUN dotnet restore "VDF.Web/VDF.Web.csproj"
COPY VDF.Core/ VDF.Core/
COPY VDF.Web/ VDF.Web/
RUN dotnet publish "VDF.Web/VDF.Web.csproj" -c Release -o /app/publish --no-restore

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Install FFmpeg and curl (curl needed for HEALTHCHECK)
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg curl && rm -rf /var/lib/apt/lists/*
# Create non-root user and ensure config/state directories are writable
RUN groupadd -r vdf && useradd -r -g vdf -d /app -s /sbin/nologin vdf
RUN mkdir -p /app/.config/VDF /app/.local/state/VDF
WORKDIR /app
COPY --from=backend /app/publish .
# Copy React SPA build output to wwwroot
COPY --from=frontend /client/dist ./wwwroot
# Set ownership
RUN chown -R vdf:vdf /app
USER vdf
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1
# Labels for reverse proxy compatibility
LABEL org.opencontainers.image.title="Video Duplicate Finder" \
      org.opencontainers.image.description="Web UI for finding duplicate video files" \
      vdf.reverse-proxy.compat="true" \
      vdf.base-path-env="VDF_BASE_PATH"
ENTRYPOINT ["dotnet", "VDF.Web.dll"]
