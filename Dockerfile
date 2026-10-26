# 1. Use the official .NET SDK image to build the application
FROM ://microsoft.com AS build-env
WORKDIR /app

# 2. Copy the solution file and restore project dependencies
COPY *.sln ./
COPY src/DriveShield.Api/*.csproj ./src/DriveShield.Api/
RUN dotnet restore

# 3. Copy the rest of the source files and build the release package
COPY src/ ./src/
WORKDIR /app/src/DriveShield.Api
RUN dotnet publish -c Release -o /app/out

# 4. Build the final runtime image using a lightweight runtime environment
FROM ://microsoft.com
WORKDIR /app
COPY --from=build-env /app/out .

# 5. Render dynamically changes the port, so we instruct ASP.NET to listen on any port provided
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "DriveShield.Api.dll"]
