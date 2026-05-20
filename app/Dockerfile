FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY HospitalApi/HospitalApi.csproj HospitalApi/
RUN dotnet restore HospitalApi/HospitalApi.csproj
COPY HospitalApi/ HospitalApi/
RUN dotnet publish HospitalApi/HospitalApi.csproj -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/out .
EXPOSE 8080
ENTRYPOINT ["dotnet", "HospitalApi.dll"]
