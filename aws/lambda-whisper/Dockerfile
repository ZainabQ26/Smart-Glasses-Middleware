FROM public.ecr.aws/lambda/python:3.11

ENV PYTHONUNBUFFERED=1

# Install minimal build/runtime tools and curl. Use yum (Amazon Linux base).
RUN yum -y update && \
    yum -y install --setopt=tsflags=nodocs \
        curl \
        tar \
        xz \
        gcc \
        gcc-c++ \
        make \
        which && \
    yum clean all

# Install a static ffmpeg build (no distro repo dependency).
RUN curl -L -o /tmp/ffmpeg-release-amd64-static.tar.xz https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz \
    && tar -xJf /tmp/ffmpeg-release-amd64-static.tar.xz -C /tmp \
    && cp /tmp/ffmpeg-*-amd64-static/ffmpeg /usr/local/bin/ffmpeg \
    && cp /tmp/ffmpeg-*-amd64-static/ffprobe /usr/local/bin/ffprobe \
    && chmod +x /usr/local/bin/ffmpeg /usr/local/bin/ffprobe \
    && rm -rf /tmp/ffmpeg-release-amd64-static.tar.xz /tmp/ffmpeg-*-amd64-static

WORKDIR /app

COPY requirements.txt .
# Ensure pip is recent, then install torch and other Python deps.
# Use --prefer-binary to avoid source builds that require Rust/GCC toolchains
# (tiktoken has prebuilt wheels; building from source needs rustc).
RUN python -m pip install --upgrade pip && \
    pip install --no-cache-dir torch==2.0.0 --index-url https://download.pytorch.org/whl/cpu && \
    pip install --no-cache-dir --prefer-binary -r requirements.txt

COPY handler.py .

# Preserve existing behavior: run handler as a normal Python process inside the
# container. If you intend to use this image as a Lambda container image, you
# can change CMD to ["handler.lambda_handler"] to use the Lambda runtime entry.
CMD ["/usr/local/bin/python", "handler.py"]
