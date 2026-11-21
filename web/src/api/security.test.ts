import { describe, expect, it } from "vitest";
import { isIpEntry, matchingRoot, normalizeRoot } from "./security";

const roots = ["capu.edu.eg", "helwan.edu.eg"];

describe("university email domains (ADR-0016)", () => {
  it.each([
    ["username@capu.edu.eg", "capu.edu.eg"],
    ["username@eng.capu.edu.eg", "capu.edu.eg"],
    ["username@med.capu.edu.eg", "capu.edu.eg"],
    ["username@commerce.capu.edu.eg", "capu.edu.eg"],
    ["Username@Subdomain.CAPU.edu.eg", "capu.edu.eg"],
    ["username@helwan.edu.eg", "helwan.edu.eg"],
    ["username@eng.helwan.edu.eg", "helwan.edu.eg"],
    ["username@subdomain.helwan.edu.eg", "helwan.edu.eg"],
  ])("accepts %s under %s", (email, root) => expect(matchingRoot(email, roots)).toBe(root));

  it.each(["username@gmail.com", "username@xcapu.edu.eg", "username@capu.edu.eg.evil.com", "username@edu.eg", "username@-x.capu.edu.eg", "capu.edu.eg"])(
    "refuses %s",
    (email) => expect(matchingRoot(email, roots)).toBeNull(),
  );

  it("a new root covers its subdomains at once", () => {
    expect(matchingRoot("user@any-subdomain.alex.edu.eg", [...roots, "alex.edu.eg"])).toBe("alex.edu.eg");
  });

  it("stores a root as the server does, and refuses an invalid one", () => {
    expect(normalizeRoot(" @Alex.EDU.eg. ")).toBe("alex.edu.eg");
    for (const bad of ["", "localhost", "*.capu.edu.eg", "capu edu.eg", "capu.edu.123"]) expect(normalizeRoot(bad)).toBeNull();
  });
});

describe("IP allowlist entries (ADR-0016)", () => {
  it.each(["192.168.1.10", "41.65.100.20", "192.168.1.0/24", "41.65.100.0/24", "2001:db8::1", "2001:db8::/32", "::1"])("accepts %s", (v) =>
    expect(isIpEntry(v)).toBe(true));

  it.each(["", "300.1.1.1", "10", "10.1", "192.168.1.0/33", "192.168.1.0/0", "192.168.1.0/x", "example.com", "1::2::3", "a/b/c"])("refuses %s", (v) =>
    expect(isIpEntry(v)).toBe(false));
});
