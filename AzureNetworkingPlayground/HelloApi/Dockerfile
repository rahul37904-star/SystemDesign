FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY api-publish/ .
ENV ASPNETCORE_HTTPS_PORTS=443
ENTRYPOINT ["dotnet", "HelloApi.dll"]
