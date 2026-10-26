# Stage 1: Build using the full SDK
FROM ://microsoft.com AS build-env
WORKDIR /app

COPY *.sln ./
COPY src/DriveShield.Api/*.csproj ./src/DriveShield.Api/
RUN dotnet restore

COPY src/ ./src/
WORKDIR /app/src/DriveShield.Api
RUN dotnet publish -c Release -o /app/out

# Stage 2: Run using the smaller ASP.NET runtime (Notice the "aspnet" instead of "sdk")
FROM ://microsoft.com
WORKDIR /app
COPY --from=build-env /app/out .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "DriveShield.Api.dll"]
