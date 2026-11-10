import type { ReactNode } from "react";
import { Alert, Button, Result, Skeleton } from "antd";
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

/** تنبيه خطأ برسالة الخادم وزر «أعد المحاولة». */
export function QueryError({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
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

/** شاشة كاملة حين يتعذر التحقق من الجلسة لخطأ في الخادم أو الشبكة؛ ليست خروجًا فلا تعرض صفحة الدخول. */
export function ErrorScreen({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  return (
    <div className="center-screen">
      <Result status="warning" title={L("تعذر التحقق من جلستك", "Could not check your session")} subTitle={errorText(error)}
        extra={<Button type="primary" onClick={onRetry}>{L("أعد المحاولة", "Retry")}</Button>} />
    </div>
  );
}
