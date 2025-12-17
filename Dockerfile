FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
COPY . /build
WORKDIR /build
RUN dotnet restore
RUN dotnet publish SMTP2Graph -c Release -o /SMTP2Graph --no-restore
FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /SMTP2Graph
COPY --from=build /SMTP2Graph .
ENV TENANT_ID=""
ENV CLIENT_ID=""
ENV CLIENT_SECRET=""
ENTRYPOINT ./SMTP2Graph