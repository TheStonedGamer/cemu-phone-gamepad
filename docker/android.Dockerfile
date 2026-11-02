FROM eclipse-temurin:21-jdk-jammy
RUN apt-get update && apt-get install -y --no-install-recommends curl unzip zip && rm -rf /var/lib/apt/lists/*
ENV ANDROID_HOME=/opt/android-sdk
RUN mkdir -p /opt/android-sdk/cmdline-tools && curl -fsSL https://dl.google.com/android/repository/commandlinetools-linux-13114758_latest.zip -o /tmp/tools.zip && echo '5fdcc763663eefb86a5b8879697aa6088b041e70  /tmp/tools.zip' | sha1sum -c - && unzip -q /tmp/tools.zip -d /opt/android-sdk/cmdline-tools && mv /opt/android-sdk/cmdline-tools/cmdline-tools /opt/android-sdk/cmdline-tools/19.0 && rm /tmp/tools.zip
RUN yes | /opt/android-sdk/cmdline-tools/19.0/bin/sdkmanager --licenses >/dev/null
RUN /opt/android-sdk/cmdline-tools/19.0/bin/sdkmanager 'platforms;android-35' 'build-tools;35.0.0'
WORKDIR /project/android
