import { Button, Dropdown } from "antd";
import { BranchesOutlined } from "@ant-design/icons";
import { L } from "./i18n";

const roles = [
  ["planner", "مدير متابعة الخطة", "Plan manager"],
  ["entry", "مدخل بيانات الكلية", "College data entry"],
  ["deptrep", "ممثل قسم المدنية", "Civil engineering representative"],
  ["rep.ele", "ممثل قسم الكهربية", "Electrical engineering representative"],
  ["rep.mec", "ممثل قسم الميكانيكية", "Mechanical engineering representative"],
  ["college", "لجنة الكلية", "College committee"],
  ["dean", "عميد الكلية", "Dean"],
  ["quality", "الجودة المركزية", "Central quality"],
  ["entry.cs", "مدخل بيانات الحاسبات", "Computing data entry"],
  ["college.cs", "لجنة كلية الحاسبات", "Computing college committee"],
  ["dean.cs", "عميد كلية الحاسبات", "Computing dean"],
  ["rep.se", "ممثل قسم هندسة البرمجيات", "Software engineering representative"],
  ["rep.ai", "ممثل قسم الذكاء الاصطناعي", "AI department representative"],
  ["entry.uni", "مدخل بيانات الجامعة", "University data entry"],
  ["president", "رئيس الجامعة", "University president"],
  ["committee", "اللجنة العليا", "Higher committee"],
  ["tier1", "مسؤول ملف Tier 1", "Tier 1 file officer"],
  ["sa1", "المدير الأعلى الأول", "First super admin"],
  ["sa2", "المدير الأعلى الثاني (sa2)", "Second super admin (sa2)"],
] as const;

function enabled() {
  if (!import.meta.env.DEV || import.meta.env.VITE_DEV_ROLE_TABS !== "true") return false;
  const host = window.location.hostname;
  return host === "127.0.0.1" || host === "localhost" || host.endsWith(".localhost");
}

export function devRoleEmail() {
  if (!enabled()) return undefined;
  const role = roles.find(([name]) => window.location.hostname === `${name}.localhost`);
  return role ? `${role[0]}@tier1.local` : undefined;
}

export function DevRoleTabs() {
  if (!enabled()) return null;
  const label = L("تبويبات الأدوار (تطوير)", "Role tabs (dev)");
  const items = roles.map(([name, ar, en]) => {
    const url = new URL("/login", window.location.origin);
    // Host-only cookies keep sessions separate while every alias uses the same API and database.
    url.hostname = `${name}.localhost`;
    return {
      key: name,
      label: <a href={url.href} target="_blank" rel="noopener">{L(ar, en)}</a>,
    };
  });
  return (
    <Dropdown trigger={["click"]} menu={{ items }}>
      <Button type="text" icon={<BranchesOutlined />} aria-label={label} title={label} />
    </Dropdown>
  );
}
