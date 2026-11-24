# One Dockerfile for the three services: PROJECT selects which one to publish (Legacy.Api, Modern.Api or Gateway).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
RUN dotnet publish "src/${PROJECT}/${PROJECT}.csproj" -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG PROJECT
WORKDIR /app
COPY --from=build /app ./
RUN mkdir -p /app/data && chown "$APP_UID" /app/data
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    SERVICE_DLL="${PROJECT}.dll" \
    ConnectionStrings__Shop="Data Source=/app/data/shop.db"
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet \"$SERVICE_DLL\""]
