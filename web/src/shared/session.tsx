import { createContext, useContext, type ReactNode } from "react";
import { useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { api, ApiError, refreshCsrf } from "../api/client";
import type { CapLetter, Effective, Me } from "../api/types";

interface SessionState {
  me: Me | null;
  loading: boolean;
  /** خطأ خادم أو شبكة قبل معرفة الجلسة؛ 401 و403 ليسا خطأ بل «بلا جلسة». */
  error: Error | null;
  retry: () => void;
}

const SessionContext = createContext<SessionState>({ me: null, loading: true, error: null, retry: () => undefined });

export function SessionProvider({ children }: { children: ReactNode }) {
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: ["me"],
    queryFn: async () => {
      try {
        return await api.get<Me>("/api/me");
      } catch (e) {
        if (e instanceof ApiError && (e.status === 401 || e.status === 403)) return null;
        throw e;
      }
    },
    staleTime: 30_000,
    refetchInterval: 60_000,
  });
  // فشل تحديث لاحق لا يُخرج المستخدم: تبقى الجلسة المعروفة.
  const value: SessionState = { me: data ?? null, loading: isLoading, error: data === undefined ? error : null, retry: () => void refetch() };
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession() {
  return useContext(SessionContext);
}

/** يعيد قراءة الجلسة ورمز CSRF بعد الدخول أو الخروج أو أي خطوة تحقق. */
export async function reloadSession(qc: QueryClient) {
  await refreshCsrf();
  await qc.invalidateQueries();
  await qc.refetchQueries({ queryKey: ["me"] });
}

export function useReloadSession() {
  const qc = useQueryClient();
  return () => reloadSession(qc);
}

export function can(p: Effective | undefined, feature: string, cap: CapLetter | string): boolean {
  if (!p) return false;
  const f = p.features[feature];
  if (!f) return false;
  return cap.length === 1 ? f.caps[cap as CapLetter] !== undefined : f.specials.includes(cap);
}

/** صلاحيات المستخدم الحالي للواجهة فقط؛ الخادم يبقى الحكم في كل طلب. */
export function usePermissions() {
  const { me } = useSession();
  const p = me?.permissions;
  return {
    can: (feature: string, cap: CapLetter | string) => can(p, feature, cap),
    scopeOf: (feature: string, cap: CapLetter) => p?.features[feature]?.caps[cap],
    isSuperAdmin: p?.isSuperAdmin ?? false,
    roles: p?.roles ?? [],
  };
}
