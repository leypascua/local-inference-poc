FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . ./
RUN dotnet restore "./invoice-extraction-api.csproj"
RUN dotnet publish "./invoice-extraction-api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:9998
COPY --from=build /app/publish ./

EXPOSE 9998
ENTRYPOINT ["dotnet", "invoice-extraction-api.dll"]
