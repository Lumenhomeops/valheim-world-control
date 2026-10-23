FROM ubuntu:22.04

LABEL org.opencontainers.image.title="Valheim World Control runtime" \
      org.opencontainers.image.source="https://github.com/Lumenhomeops/valheim-world-control"

RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        ca-certificates \
        libatomic1 \
        libpulse0 \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 1000 valheim \
    && useradd --uid 1000 --gid 1000 --create-home --home-dir /home/valheim valheim

COPY --chmod=0555 entrypoint.sh /usr/local/bin/valheim-world-control-entrypoint

USER valheim
WORKDIR /opt/valheim
ENTRYPOINT ["/usr/local/bin/valheim-world-control-entrypoint"]
