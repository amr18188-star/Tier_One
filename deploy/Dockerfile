# صورة الإنتاج: الواجهة تبنى بـ Node، والخادم ينشر بـ .NET، والتشغيل على صورة ASP.NET الصغيرة بمستخدم غير الجذر.
# البناء من جذر المستودع:  docker build -f deploy/Dockerfile -t tier1:latest .
# صورة الأدوات (أوامر الصيانة والتهيئة، خارج الخادم): docker build -f deploy/Dockerfile --target tools -t tier1-tools:latest .

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
# The embedded KPI workbook template (reference/KPI.xlsx).
COPY reference/KPI.xlsx reference/KPI.xlsx
RUN dotnet publish src/Tier1.Api/Tier1.Api.csproj -c Release -o /out/app /p:UseAppHost=false

# ARCH-4: the operations tools (data fixes, structure loading, Mongo setup, imports) in an image of their own, never in the server's.
#   docker build -f deploy/Dockerfile --target tools -t tier1-tools:latest .
FROM api AS tools-build
COPY tools/ tools/
COPY tests/Tier1.TestSupport/ tests/Tier1.TestSupport/
RUN dotnet publish tools/Tier1.Tools/Tier1.Tools.csproj -c Release -o /out/tools /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS tools
RUN apt-get update && apt-get install -y --no-install-recommends tzdata && rm -rf /var/lib/apt/lists/*
WORKDIR /tools
COPY --from=tools-build /out/tools ./
USER app
ENV DOTNET_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "Tier1.Tools.dll"]

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
