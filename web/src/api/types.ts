
export type CapLetter = "V" | "P" | "E" | "A" | "D";
export type ScopeCode = "A" | "U" | "S" | "O";

export const CAPS: CapLetter[] = ["V", "P", "E", "A", "D"];

export interface Named {
  key: string;
  name: string;
}

export interface Feature {
  key: string;
  module: string;
  name: string;
  caps: { cap: CapLetter; meaning: string }[];
  specials: Named[];
}

export interface Catalog {
  modules: Named[];
  features: Feature[];
  scopes: Named[];
  defaultRoles: { code: string; name: string; description: string; isNew: boolean }[];
}

export interface FeatureAccess {
  caps: Partial<Record<CapLetter, ScopeCode>>;
  specials: string[];
  scopeValues: string[];
}

export interface Effective {
  isSuperAdmin: boolean;
  roles: string[];
  features: Record<string, FeatureAccess>;
}

export interface Me {
  id: string;
  email: string;
  name: string;
  fullNameAr: string;
  fullNameEn: string | null;
  language: string;
  mfaEnabled: boolean;
  mfaPending: boolean;
  mustChangePassword: boolean;
  permissions: Effective;
}

/** منح بصيغة العرض: حروف الإمكانيات ورمز النطاق والإجراءات الخاصة. */
export interface GrantView {
  caps: string;
  scope: ScopeCode;
  specials: string[];
}

export interface RoleSummary {
  id: string;
  code: string;
  name: string;
  nameEn: string | null;
  description: string | null;
  isSystem: boolean;
  isLocked: boolean;
  grantsVersion: number;
  holders: number;
  pendingChangeId: string | null;
}

export interface RoleDetail {
  role: RoleSummary;
  grants: Record<string, GrantView>;
}

export interface Person {
  id: string;
  name: string;
}

export interface GrantDiff {
  feature: string;
  before: GrantView | null;
  after: GrantView | null;
}

export type ChangeStatus = "Pending" | "Approved" | "Rejected" | "Cancelled" | "Stale";

export interface RbacChange {
  id: string;
  roleId: string;
  roleCode: string;
  roleName: string;
  requestedBy: Person;
  requestedAt: string;
  reason: string;
  baseVersion: number;
  status: ChangeStatus;
  decidedBy: Person | null;
  decidedAt: string | null;
  decisionNote: string | null;
  diff: GrantDiff[];
  canDecide: boolean;
  canCancel: boolean;
}

export interface UserListItem {
  id: string;
  email: string;
  name: string;
  active: boolean;
  mfaEnabled: boolean;
  locked: boolean;
  lastLoginAt: string | null;
  roles: string[];
  /** ADR-0021: ينقص الحساب شيء من بياناته الإلزامية (الاسم بالإنجليزية أو الرقم القومي أو رقم المحمول). */
  incomplete?: boolean;
}

export interface Assignment {
  id: string;
  roleId: string;
  roleCode: string;
  roleName: string;
  scopeValues: string[];
  validFrom: string;
  validTo: string | null;
  grantedBy: Person | null;
  grantedAt: string;
  reason: string | null;
  revokedAt: string | null;
  revokeReason: string | null;
  active: boolean;
  /** إسناد دور المدير الأعلى ينتظر اعتماد مدير أعلى مستقل. */
  pendingApproval: boolean;
  approvedBy: Person | null;
  canApprove: boolean;
}

export interface Override {
  id: string;
  feature: string;
  caps: string;
  specials: string[];
  effect: "Allow" | "Deny";
  scope: ScopeCode;
  validFrom: string;
  validTo: string | null;
  grantedBy: Person | null;
  reason: string;
  revokedAt: string | null;
  active: boolean;
}

export type TitleGroup = "Faculty" | "Staff" | "Officer";
/** لقب في قائمة الاختيار (ADR-0019): `title` نصه العربي (القيمة المحفوظة بأي لغة)، و`label` و`meaning` ما يعرض بلغة الواجهة. */
export interface TitleOption { title: string; label: string; meaning: string; group: TitleGroup }

export interface UserDetail {
  id: string;
  email: string;
  fullNameAr: string;
  fullNameEn: string | null;
  /** اللقب قبل الاسم، أو لا شيء لحساب قديم. */
  title?: string | null;
  /** الاسم كما يعرض: اللقب ثم الاسم ثنائيًا. */
  displayName?: string | null;
  /** رقم المحمول بشكله المحفوظ (ADR-0021). */
  phone?: string | null;
  /** الرقم القومي مقنّعًا: آخر أربعة أرقام فقط. الرقم الكامل لا يخرج من الخادم. */
  nationalIdMasked?: string | null;
  /** ما ينقص الحساب من بياناته الإلزامية بأسماء حقولها؛ فارغة حين تكتمل. */
  missingData?: string[] | null;
  active: boolean;
  mfaEnabled: boolean;
  mustChangePassword: boolean;
  lockedUntil: string | null;
  createdAt: string;
  lastLoginAt: string | null;
  /** يحمل دورًا محجوزًا: لا يديره إلا المدير الأعلى. */
  holdsReservedRole: boolean;
  assignments: Assignment[];
  overrides: Override[];
}

export interface LogItem {
  id: string;
  ts: string;
  collection: string;
  module: string | null;
  feature: string | null;
  action: string | null;
  outcome: string | null;
  message: string | null;
  actor: { userId: string | null; name: string | null; roles: string[]; type: string | null } | null;
  target: { type: string | null; id: string | null; ref: string | null; ownerId: string | null } | null;
  related: { type: string | null; id: string | null; ref: string | null }[];
  changes: { field: string; old: string | null; new: string | null }[];
  reason: string | null;
  details: unknown;
  context: Record<string, string | null>;
  seq: number | null;
  hash: string | null;
}

export interface LogPage {
  items: LogItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ChainDay {
  chain: string;
  day: string;
  firstSeq: number;
  lastSeq: number;
  eventCount: number;
  lastHash: string;
  intact: boolean | null;
  brokenAtSeq: number | null;
  note: string | null;
  verifiedAt: string | null;
}

export interface LogsStatus {
  pendingOutbox: number;
  oldestPendingAt: string | null;
  days: ChainDay[];
}

