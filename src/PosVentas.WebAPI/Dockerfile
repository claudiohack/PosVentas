# API de PosVentas (.NET 10 LTS). Contexto de build: la raíz del repositorio.
#   docker build -f src/PosVentas.WebAPI/Dockerfile -t posventas-api .

# ---------- Build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primero solo los .csproj: la restauración queda en caché mientras no cambien las dependencias.
COPY Directory.Build.props ./
COPY src/PosVentas.Domain/PosVentas.Domain.csproj src/PosVentas.Domain/
COPY src/PosVentas.Application/PosVentas.Application.csproj src/PosVentas.Application/
COPY src/PosVentas.Infrastructure/PosVentas.Infrastructure.csproj src/PosVentas.Infrastructure/
COPY src/PosVentas.WebAPI/PosVentas.WebAPI.csproj src/PosVentas.WebAPI/
RUN dotnet restore src/PosVentas.WebAPI/PosVentas.WebAPI.csproj

COPY src/PosVentas.Domain/ src/PosVentas.Domain/
COPY src/PosVentas.Application/ src/PosVentas.Application/
COPY src/PosVentas.Infrastructure/ src/PosVentas.Infrastructure/
COPY src/PosVentas.WebAPI/ src/PosVentas.WebAPI/
RUN dotnet publish src/PosVentas.WebAPI/PosVentas.WebAPI.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Directorio de las claves de Data Protection (cifran los certificados de ARCA). Se monta como volumen:
# si se pierde, hay que volver a cargar los certificados de todos los comercios.
RUN mkdir -p /keys && chown "$APP_UID" /keys

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DataProtection__DirectorioClaves=/keys \
    Https__Redireccionar=false \
    Proxy__ConfiarEnEncabezados=true

COPY --from=build /app/publish .

# Usuario sin privilegios que traen las imágenes oficiales de .NET
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "PosVentas.WebAPI.dll"]
