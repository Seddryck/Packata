FROM mcr.microsoft.com/dotnet/aspnet:8.0.33-alpine3.24 AS net8
FROM mcr.microsoft.com/dotnet/aspnet:9.0.22-alpine3.24 AS net9
FROM mcr.microsoft.com/dotnet/sdk:10.0.403-alpine3.24

# Use the same .NET and ASP.NET Core runtime versions as native smoke jobs, without an installer script.
COPY --from=net8 /usr/share/dotnet/shared/Microsoft.NETCore.App/ /usr/share/dotnet/shared/Microsoft.NETCore.App/
COPY --from=net9 /usr/share/dotnet/shared/Microsoft.NETCore.App/ /usr/share/dotnet/shared/Microsoft.NETCore.App/
COPY --from=net8 /usr/share/dotnet/shared/Microsoft.AspNetCore.App/ /usr/share/dotnet/shared/Microsoft.AspNetCore.App/
COPY --from=net9 /usr/share/dotnet/shared/Microsoft.AspNetCore.App/ /usr/share/dotnet/shared/Microsoft.AspNetCore.App/

ENV DOTNET_CLI_HOME=/tmp/dotnet-cli-home
USER app
