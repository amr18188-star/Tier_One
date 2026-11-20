import { useCallback } from "react";
import { useNavigate } from "react-router-dom";

/**
 * A page with unsaved work (the form page's autosave, UX-3) can ask before the app moves away. Links are caught by the page
 * itself; navigation from code (the global search, UX) goes through `useGuardedNavigate`, which asks the same guard.
 * The guard returns true when it takes over: it then calls `proceed` itself if the user leaves, or never if they stay.
 */
export type LeaveGuard = (proceed: () => void) => boolean;

let current: LeaveGuard | null = null;

/** Sets the guard while a page has unsaved work; returns the function that removes it (only if it is still the one set). */
export function setLeaveGuard(guard: LeaveGuard): () => void {
  current = guard;
  return () => {
    if (current === guard) current = null;
  };
}

/** Runs `go` now, or lets the page with unsaved work decide first. */
export function leaveThen(go: () => void): void {
  if (current?.(go)) return;
  go();
}

/** `navigate(to)` that asks the page with unsaved work first, as a link click does. */
export function useGuardedNavigate(): (to: string) => void {
  const navigate = useNavigate();
  return useCallback((to: string) => leaveThen(() => navigate(to)), [navigate]);
}
