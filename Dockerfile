# To pin to an immutable digest (recommended for production), run:
#   docker pull mcr.microsoft.com/dotnet/sdk:10.0
#   docker inspect mcr.microsoft.com/dotnet/sdk:10.0 --format '{{index .RepoDigests 0}}'
# Then replace the tags below with the digest, e.g.:
#   FROM mcr.microsoft.com/dotnet/sdk@sha256:<digest>
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY *.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# To pin the runtime image:
#   docker pull mcr.microsoft.com/dotnet/aspnet:10.0
#   docker inspect mcr.microsoft.com/dotnet/aspnet:10.0 --format '{{index .RepoDigests 0}}'
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MyPasswordVault.API.dll"]
