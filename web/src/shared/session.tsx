import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from "react";
import { useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { api, ApiError, onSessionEnded, refreshCsrf } from "../api/client";
import type { CapLetter, Effective, Me } from "../api/types";
import { queryKeys } from "../api/queries";

/** Fired after the user signs in again over the page they were on (UX-1); unsaved rows retry their saves. */
export const SESSION_RESUMED = "tier1:session-resumed";

/** What a sign-in over an ended session led to: back at work, a code to enter, or a full reload (another account or a setup step). */
export type ResumeResult = "resumed" | "code" | "reload";

interface SessionState {
  me: Me | null;
  loading: boolean;
  /** خطأ خادم أو شبكة قبل معرفة الجلسة؛ 401 و403 ليسا خطأ بل «بلا جلسة». */
  error: Error | null;
  retry: () => void;
  /**
   * UX-1/FE-3: the session ended while the user was working. The page stays mounted under a sign-in dialog, so typed values
   * survive; `me` keeps the last known user until they sign in again.
   */
  expired: boolean;
  resume: () => Promise<ResumeResult>;
}

const SessionContext = createContext<SessionState>({
  me: null, loading: true, error: null, retry: () => undefined, expired: false, resume: async () => "reload",
});

export function SessionProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient();
  const [expired, setExpired] = useState(false);
  const known = useRef<Me | null>(null);
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: queryKeys.me,
    queryFn: async () => {
      try {
        // FE-2: the minute poll is passive, so an unattended tab still reaches the idle timeout.
        return await api.poll<Me>("/api/me");
      } catch (e) {
        if (e instanceof ApiError && (e.status === 401 || e.status === 403)) return null;
        throw e;
      }
    },
    staleTime: 30_000,
    refetchInterval: 60_000,
  });
  if (data && !data.mfaPending && !data.mustChangePassword) known.current = data;

  // Any call answered 401 after the user was signed in, or the poll finding no session: the session ended under them.
  useEffect(() => {
    onSessionEnded(() => {
      if (known.current) setExpired(true);
    });
    return () => onSessionEnded(null);
  }, []);
  useEffect(() => {
    if (data === null && known.current) setExpired(true);
  }, [data]);

  const resume = useCallback(async (): Promise<ResumeResult> => {
    await refreshCsrf();
    const fresh = await qc.fetchQuery({ queryKey: queryKeys.me, queryFn: () => api.get<Me>("/api/me"), staleTime: 0 });
    // Another account must not inherit the page (its unsaved values would be saved in the other person's name).
    if (!fresh || fresh.id !== known.current?.id || fresh.mustChangePassword || (fresh.mfaPending && !fresh.mfaEnabled)) return "reload";
    if (fresh.mfaPending) return "code";
    known.current = fresh;
    setExpired(false);
    await qc.invalidateQueries({ predicate: (q) => q.queryKey[0] !== "me" });
    window.dispatchEvent(new Event(SESSION_RESUMED));
    return "resumed";
  }, [qc]);

  // فشل تحديث لاحق لا يُخرج المستخدم: تبقى الجلسة المعروفة.
  const value: SessionState = {
    me: expired ? known.current : data ?? null,
    loading: isLoading,
    error: data === undefined && !expired ? error : null,
    retry: () => void refetch(),
    expired,
    resume,
  };
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession() {
  return useContext(SessionContext);
}

/**
 * يعيد قراءة الجلسة ورمز CSRF بعد الدخول أو الخروج أو أي خطوة تحقق.
 * إن تغيرت هوية المستخدم (انتهت الجلسة ثم دخل غيره في التبويب نفسه) تحذف كل البيانات المخزنة، لا تعلم «قديمة» فقط:
 * التعليم وحده يبقي بيانات المستخدم السابق ظاهرة حتى يعاد جلبها، وإلى الأبد لما لا يملك الجديد صلاحية جلبه.
 */
export async function reloadSession(qc: QueryClient) {
  await refreshCsrf();
  await refreshIdentity(qc, () => qc.refetchQueries({ queryKey: queryKeys.me }));
}

/** يجلب الجلسة بـ fetchMe؛ هوية مختلفة عما قبلها تحذف ما سوى ["me"]، والهوية نفسها تعلم الكل قديمًا ليعاد جلبه. */
export async function refreshIdentity(qc: QueryClient, fetchMe: () => Promise<unknown>) {
  const before = qc.getQueryData<Me | null>(["me"])?.id ?? null;
  await fetchMe();
  const after = qc.getQueryData<Me | null>(["me"])?.id ?? null;
  if (before !== after) qc.removeQueries({ predicate: (q) => q.queryKey[0] !== "me" });
  else await qc.invalidateQueries({ predicate: (q) => q.queryKey[0] !== "me" });
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
