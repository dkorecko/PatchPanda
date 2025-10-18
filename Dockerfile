FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["PatchPanda.Web/PatchPanda.Web.csproj", "PatchPanda.Web/"]
RUN dotnet restore "PatchPanda.Web/PatchPanda.Web.csproj"
COPY . .
RUN dotnet build "PatchPanda.Web/PatchPanda.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PatchPanda.Web/PatchPanda.Web.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PatchPanda.Web.dll"]