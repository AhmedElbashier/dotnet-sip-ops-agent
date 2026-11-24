# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Build.props
COPY SipOpsAgent.sln SipOpsAgent.sln
COPY src/SipOpsAgent/SipOpsAgent.csproj src/SipOpsAgent/SipOpsAgent.csproj
COPY tests/SipOpsAgent.Tests/SipOpsAgent.Tests.csproj tests/SipOpsAgent.Tests/SipOpsAgent.Tests.csproj
RUN dotnet restore SipOpsAgent.sln
COPY . .
RUN dotnet publish src/SipOpsAgent/SipOpsAgent.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
RUN adduser --disabled-password --gecos "" appuser
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER appuser
ENTRYPOINT ["dotnet", "SipOpsAgent.dll"]
