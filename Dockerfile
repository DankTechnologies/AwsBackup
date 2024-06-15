FROM mcr.microsoft.com/dotnet/aspnet:8.0

ENV DEBIAN_FRONTEND=noninteractive
ENV DOTNET_ENVIRONMENT=Production

RUN apt-get update && \
    apt-get install -y tzdata gpg tar && \
    apt-get clean

WORKDIR /app
COPY bin/Release/net8.0/publish  ./

ENTRYPOINT ["dotnet", "AwsBackup.dll"]