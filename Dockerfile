FROM mcr.microsoft.com /dotnet/sdk:9.0 AS build-env
WORKDIR /app

COPY *.sln ./
COPY src/DriveShield.Api/*.csproj ./src/DriveShield.Api/
RUN dotnet restore

COPY src/ ./src/
WORKDIR /app/src/DriveShield.Api
RUN dotnet publish -c Release -o /app/out

FROM mcr.microsoft.com /dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build-env /app/out .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "DriveShield.Api.dll"]
