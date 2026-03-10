# Gluey CLI Dockerfile
# Multi-stage build for minimal image size

# Stage 1: Build
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.102 AS build
ARG TARGETARCH
WORKDIR /src

# Map Docker TARGETARCH to .NET RID
RUN case "$TARGETARCH" in \
      amd64) echo "linux-x64" > /tmp/rid ;; \
      arm64) echo "linux-arm64" > /tmp/rid ;; \
      *) echo "linux-$TARGETARCH" > /tmp/rid ;; \
    esac

# Copy solution and project files first (for layer caching)
COPY Gluey.sln .
COPY src/Gluey.Cli/Gluey.Cli.csproj src/Gluey.Cli/
COPY src/Gluey.Core/Gluey.Core.csproj src/Gluey.Core/
COPY src/Gluey.Parser/Gluey.Parser.csproj src/Gluey.Parser/
COPY src/Gluey.Runtime/Gluey.Runtime.csproj src/Gluey.Runtime/
COPY src/Gluey.Plugins/Gluey.Plugins.csproj src/Gluey.Plugins/
COPY tests/Gluey.Parser.Tests/Gluey.Parser.Tests.csproj tests/Gluey.Parser.Tests/
COPY Directory.Build.props .

# Restore dependencies for target platform
RUN dotnet restore -r "$(cat /tmp/rid)"

# Copy source code
COPY src/ src/
COPY tests/ tests/
COPY samples/ samples/

# Build and publish release
RUN dotnet publish src/Gluey.Cli/Gluey.Cli.csproj \
    -c Release \
    -r "$(cat /tmp/rid)" \
    -o /app/publish \
    --no-restore \
    /p:PublishSingleFile=true

# Stage 2: Runtime
# Using aspnet base because Gluey.Plugins uses Microsoft.AspNetCore.App for HTTP input
FROM mcr.microsoft.com/dotnet/aspnet:10.0.2 AS runtime
WORKDIR /app

# Copy published app
COPY --from=build /app/publish .

# Rename to 'gluey' for cleaner CLI usage
RUN mv Gluey.Cli gluey || true

# Set entrypoint
ENTRYPOINT ["./gluey"]
CMD ["--help"]
