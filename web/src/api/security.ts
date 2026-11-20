/**
 * ADR-0016: approved university email domains and the IP allowlists, as the API returns them, and the same checks the server
 * makes, so a form marks a mistake before sending. The server decides in the end (it validates everything again).
 */

export interface EmailDomain {
  id: string;
  domain: string;
  universityAr: string;
  universityEn: string | null;
  active: boolean;
  /** Accounts whose email falls under this root. */
  users: number;
  createdAt: string;
  updatedAt: string | null;
  version: number;
}

export interface IpEntry {
  id: string;
  /** A normalized network: 192.168.1.10/32, 192.168.1.0/24, 2001:db8::/32. */
  value: string;
  label: string | null;
  createdAt: string;
  version: number;
}

export interface IpAllowlist {
  enabled: boolean;
  entries: IpEntry[];
  /** The address of this request as the server sees it (the global list only). */
  yourAddress: string | null;
}

const LABEL = /^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/;
const TOP = /^[a-z]{2,63}$/;

/** A valid host name: two labels or more, each a DNS label, ending in a letters-only label. */
export function isValidHost(host: string): boolean {
  if (!host || host.length > 253) return false;
  const labels = host.split(".");
  return labels.length >= 2 && labels.every((l) => LABEL.test(l)) && TOP.test(labels[labels.length - 1]);
}

/** A root as typed ("@Capu.edu.eg ") in its stored form, or null when it is not a valid root. */
export function normalizeRoot(value: string): string | null {
  const root = value.trim().toLowerCase().replace(/^@+/, "").replace(/\.+$/, "");
  return isValidHost(root) ? root : null;
}

/** The approved root that covers the email (the root itself or any subdomain of it, on a label boundary), or null. */
export function matchingRoot(email: string, roots: readonly string[]): string | null {
  const at = email.trim().toLowerCase().lastIndexOf("@");
  if (at <= 0) return null;
  const domain = email.trim().toLowerCase().slice(at + 1);
  if (!isValidHost(domain)) return null;
  return [...roots].sort((a, b) => b.length - a.length).find((r) => domain === r || domain.endsWith("." + r)) ?? null;
}

const IPV4 = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/;

/** A light check of an address or CIDR range before sending; the server normalizes it and explains a wrong range start. */
export function isIpEntry(value: string): boolean {
  const text = value.trim();
  const [host, prefix, extra] = text.split("/");
  if (extra !== undefined || !host) return false;
  let bits: number;
  const v4 = IPV4.exec(host);
  if (v4) {
    if (v4.slice(1).some((p) => Number(p) > 255)) return false;
    bits = 32;
  } else if (/^[0-9a-fA-F:.]+$/.test(host) && host.includes(":") && (host.match(/::/g) ?? []).length <= 1) {
    bits = 128;
  } else return false;
  if (prefix === undefined) return true;
  if (!/^\d{1,3}$/.test(prefix)) return false;
  const n = Number(prefix);
  return n >= 1 && n <= bits;
}
