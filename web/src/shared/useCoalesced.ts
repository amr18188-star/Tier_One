import { useCallback, useEffect, useRef } from "react";

/**
 * FE-4: runs `run` once after calls stop for `delay` ms, however many calls came before. The form page uses it so a burst of
 * autosaves ends in one form refresh (completed count, blocking list, version) instead of one full GET per saved value.
 */
export function useCoalesced(run: () => void, delay: number): () => void {
  const latest = useRef(run);
  latest.current = run;
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  useEffect(() => () => clearTimeout(timer.current), []);
  return useCallback(() => {
    clearTimeout(timer.current);
    timer.current = setTimeout(() => latest.current(), delay);
  }, [delay]);
}
