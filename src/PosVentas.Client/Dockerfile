# Cliente Blazor WebAssembly servido como archivos estáticos por Nginx. Contexto de build: la raíz del repositorio.
#   docker build -f src/PosVentas.Client/Dockerfile -t posventas-frontend .

# ---------- Build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/PosVentas.Client/PosVentas.Client.csproj src/PosVentas.Client/
RUN dotnet restore src/PosVentas.Client/PosVentas.Client.csproj

COPY src/PosVentas.Client/ src/PosVentas.Client/
RUN dotnet publish src/PosVentas.Client/PosVentas.Client.csproj -c Release -o /app/publish --no-restore

# ---------- Runtime ----------
FROM nginx:1.27-alpine AS runtime
COPY docker/frontend/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
EXPOSE 80
