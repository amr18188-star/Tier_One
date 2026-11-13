import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { fileURLToPath, URL } from "node:url";

/**
 * مكتبات الطرف الثالث في حزم ثابتة منفصلة عن شيفرة التطبيق، فتبقى في ذاكرة المتصفح بين الإصدارات.
 * ملفات لغة antd الإنجليزية بنسخة CommonJS لا تدخل حزمة antd: تحمل وحدها عند اختيار الإنجليزية.
 */
function vendorChunk(id: string): string | undefined {
  if (!id.includes("node_modules")) return undefined;
  if (/en_US\.js/.test(id) && !/[\\/]es[\\/]/.test(id)) return undefined;
  if (/[\\/]node_modules[\\/](react|react-dom|scheduler|react-router|react-router-dom)[\\/]/.test(id)) return "react";
  if (/[\\/]node_modules[\\/](antd|@ant-design[\\/][^\\/]+|rc-[^\\/]+|@rc-component[\\/][^\\/]+)[\\/]/.test(id)) return "antd";
  if (/[\\/]node_modules[\\/]@tanstack[\\/]/.test(id)) return "query";
  if (/[\\/]node_modules[\\/]dayjs[\\/]/.test(id)) return "dayjs";
  if (/[\\/]node_modules[\\/]libphonenumber-js[\\/]/.test(id)) return "libphonenumber";
  return undefined;
}

// الواجهة تبنى إلى wwwroot في الخادم؛ في التطوير يمرر Vite طلبات /api إلى الخادم المحلي.
// داخل Docker (docker-compose.dev.yml) يحدد VITE_API_TARGET عنوان خدمة الخادم، وVITE_USE_POLLING فحص الملفات الدوري للمجلد المركب.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) } },
  server: {
    port: 5188,
    strictPort: true,
    watch: process.env.VITE_USE_POLLING ? { usePolling: true } : undefined,
    proxy: { "/api": { target: process.env.VITE_API_TARGET ?? "http://127.0.0.1:5150", changeOrigin: false } },
  },
  build: {
    outDir: "../src/Tier1.Api/wwwroot",
    emptyOutDir: true,
    // حزمة antd وحدها نحو 1.26 ميجابايت (395 كيلوبايت مضغوطة) في ملف مستقل يخبئه المتصفح بين الإصدارات؛ ما سواها تحت هذا الحد بكثير.
    chunkSizeWarningLimit: 1300,
    rollupOptions: {
      output: { manualChunks: vendorChunk },
    },
  },
  test: { environment: "node" },
});
