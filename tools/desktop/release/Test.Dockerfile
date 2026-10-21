FROM ubuntu:22.04
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
    xvfb xauth fonts-dejavu-core libgl1 libglx-mesa0 libgl1-mesa-dri \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
