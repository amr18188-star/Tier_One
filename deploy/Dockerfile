# صورة الإنتاج: الواجهة تبنى بـ Node، والخادم ينشر بـ .NET، والتشغيل على صورة ASP.NET الصغيرة بمستخدم غير الجذر.
# البناء من جذر المستودع:  docker build -f deploy/Dockerfile -t tier1:latest .

FROM node:24-bookworm-slim AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npx tsc -b && npx vite build --outDir /out/wwwroot --emptyOutDir

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY global.json Directory.Build.props Tier1.sln ./
COPY src/ src/
RUN dotnet publish src/Tier1.Api/Tier1.Api.csproj -c Release -o /out/app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# بيانات المناطق الزمنية: المواعيد بتوقيت القاهرة، والخادم يرفض البدء بدونها.
RUN apt-get update && apt-get install -y --no-install-recommends tzdata && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=api /out/app ./
COPY --from=web /out/wwwroot ./wwwroot
# الملفات والمفاتيح ونقاط التثبيت على مجلدات مركبة خارج الصورة (تدخل النسخ الاحتياطي).
RUN mkdir -p /data/files /data/keys /data/checkpoints && chown -R app:app /data
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=1
# الفحص من الوكيل العكسي: /health/ready (القاعدة متاحة) و/health/live.
EXPOSE 8080
ENTRYPOINT ["dotnet", "Tier1.Api.dll"]
