FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/SpecTrace.Web/SpecTrace.Web.csproj --configuration Release --output /app -p:UseAppHost=false

FROM build AS content
WORKDIR /content
COPY corpus/ corpus/
COPY cache/ cache/
COPY runs/reference/ runs/reference/
COPY experiments/ /committed/experiments/
RUN run_id="$(sed -n 's/^ *"run_id": *"\([^"]*\)".*$/\1/p' runs/reference/manifest.json)" \
 && test -n "$run_id" \
 && mkdir -p experiments runs/web/reviews \
 && cp "/committed/experiments/$run_id.metrics.json" experiments/ \
 && if [ -f "/committed/experiments/review/$run_id.reviews.jsonl" ]; then \
      cp "/committed/experiments/review/$run_id.reviews.jsonl" runs/web/reviews/reference.jsonl; \
    fi

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
WORKDIR /srv/spectrace
COPY --from=build /app /opt/spectrace
COPY --from=content /content/ ./
RUN chown "$APP_UID" runs runs/web runs/web/reviews
ENV SPECTRACE_OFFLINE=1 \
    SPECTRACE_PUBLIC_DEMO=1 \
    PORT=8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["/bin/sh", "-c", "export ASPNETCORE_HTTP_PORTS=\"$PORT\" && exec dotnet /opt/spectrace/SpecTrace.Web.dll"]
