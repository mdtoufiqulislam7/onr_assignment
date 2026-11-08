FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["SchoolAdmission.sln", "./"]
COPY ["SchoolAdmission.Api/SchoolAdmission.Api.csproj", "SchoolAdmission.Api/"]
COPY ["SchoolAdmission.Tests/SchoolAdmission.Tests.csproj", "SchoolAdmission.Tests/"]

RUN dotnet restore "SchoolAdmission.Api/SchoolAdmission.Api.csproj"

COPY . .
WORKDIR "/src/SchoolAdmission.Api"
RUN dotnet publish "SchoolAdmission.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SchoolAdmission.Api.dll"]

