import type { ReactNode } from "react";
import { Alert, Button, Result, Skeleton } from "antd";
import { Link } from "react-router-dom";
import { ApiError } from "../api/client";
import { errorText } from "./format";
import { L } from "./i18n";

/** ما يلزم من نتيجة useQuery؛ يقبل أي استعلام دون ربط بنوع بياناته. */
export interface QueryLike {
  isLoading: boolean;
  isError: boolean;
  error: unknown;
  data?: unknown;
  refetch: () => unknown;
}

/**
 * الخطأ بحسب نوعه: لا صلاحية (403)، أو غير موجود أو خارج النطاق (404)، وكلاهما بطريق للعودة لا بزر إعادة محاولة لن يفيد؛
 * وغيرهما تنبيه برسالة الخادم وزر «أعد المحاولة».
 */
export function QueryError({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const status = error instanceof ApiError ? error.status : 0;
  if (status === 403 || status === 404)
    return (
      <Result className="lcp-state" status={status === 403 ? "403" : "404"}
        title={status === 403 ? L("لا تملك صلاحية هذه الصفحة", "You don't have access to this page") : L("غير موجود أو خارج نطاق صلاحيتك", "Not found or outside your scope")}
        subTitle={status === 403
          ? L("إن كنت تحتاجها في عملك فاطلب الصلاحية من مدير المنظومة.", "Ask the system administrator if you need it.")
          : L("ربما حذف العنصر، أو يخص جهة لا تشملها صلاحيتك.", "It may have been removed, or belongs to a unit outside your scope.")}
        extra={<Link to="/"><Button type="primary">{L("العودة إلى الرئيسية", "Back to home")}</Button></Link>} />
    );
  return (
    <Alert type="error" showIcon message={errorText(error)}
      action={onRetry && <Button size="small" onClick={onRetry}>{L("أعد المحاولة", "Retry")}</Button>} />
  );
}

/**
 * حالة استعلام أو أكثر: هيكل أثناء التحميل، وتنبيه بزر إعادة المحاولة إن فشل التحميل الأول، وإلا المحتوى.
 * الفشل مع بيانات سابقة لا يخفيها. المحتوى دالة إن كان يقرأ البيانات مباشرة فلا يحسب قبل وصولها.
 */
export function QueryState({ query, loading, children }: { query: QueryLike | QueryLike[]; loading?: ReactNode; children: ReactNode | (() => ReactNode) }) {
  const list = Array.isArray(query) ? query : [query];
  if (list.some((q) => q.isLoading)) return <>{loading === undefined ? <Skeleton active paragraph={{ rows: 3 }} /> : loading}</>;
  const failed = list.filter((q) => q.isError && q.data === undefined);
  if (failed.length > 0) return <QueryError error={failed[0].error} onRetry={() => failed.forEach((q) => void q.refetch())} />;
  return <>{typeof children === "function" ? children() : children}</>;
}

/** صفحة لا يملك المستخدم صلاحيتها (فتحها برابط مباشر): شرح وطريق للعودة بدل خطأ تحميل. */
export function NoAccess() {
  return <QueryError error={new ApiError(403, "forbidden", "")} />;
}

/** شاشة كاملة حين يتعذر التحقق من الجلسة لخطأ في الخادم أو الشبكة؛ ليست خروجًا فلا تعرض صفحة الدخول. */
export function ErrorScreen({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  return (
    <div className="center-screen">
      <Result status="warning" title={L("تعذر التحقق من جلستك", "Could not check your session")} subTitle={errorText(error)}
        extra={<Button type="primary" onClick={onRetry}>{L("أعد المحاولة", "Retry")}</Button>} />
    </div>
  );
}
