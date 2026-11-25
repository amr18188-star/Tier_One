import { Alert, App, Button, Modal, Table, Tag } from "antd";
import { LogoutOutlined } from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { api } from "../api/client";
import { queryKeys } from "../api/queries";
import { errorText, formatDateTime } from "../shared/format";
import { L } from "../shared/i18n";

interface Session { id: string; createdAt: string; lastSeenAt: string; ip: string | null; userAgent: string | null; current: boolean }

/** A short device name from the browser's user agent. */
function device(ua: string | null): string {
  if (!ua) return "—";
  const browser = /Edg\//.test(ua) ? "Edge" : /Firefox\//.test(ua) ? "Firefox" : /Chrome\//.test(ua) ? "Chrome" : /Safari\//.test(ua) ? "Safari" : L("متصفح", "Browser");
  const os = /Windows/.test(ua) ? "Windows" : /Android/.test(ua) ? "Android" : /iPhone|iPad/.test(ua) ? "iOS" : /Mac OS X/.test(ua) ? "macOS" : /Linux/.test(ua) ? "Linux" : "";
  return os ? `${browser} · ${os}` : browser;
}

/**
 * G-09 (system integration review 2026-11-24): the user's open sessions, and signing out of every device. Both endpoints
 * existed with no screen, so a user who lost a device could not end its session.
 */
export function MySessions({ onClose }: { onClose: () => void }) {
  const { modal } = App.useApp();
  const sessions = useQuery({ queryKey: queryKeys.mySessions, queryFn: () => api.get<Session[]>("/api/me/sessions") });

  const logoutAll = () => modal.confirm({
    title: L("تسجيل الخروج من كل الأجهزة؟", "Sign out of every device?"),
    content: L("تنتهي كل جلساتك، ومنها هذه، وتسجل الدخول من جديد.", "All your sessions end, this one too, and you sign in again."),
    okText: L("تسجيل الخروج من الكل", "Sign out everywhere"), okButtonProps: { danger: true }, cancelText: L("إلغاء", "Cancel"),
    onOk: async () => {
      try {
        await api.post("/api/sessions/logout-all");
      } finally {
        window.location.replace("/login");
      }
    },
  });

  return (
    <Modal open title={L("جلساتي", "My sessions")} onCancel={onClose} width={760} destroyOnHidden
      footer={<>
        <Button onClick={onClose}>{L("إغلاق", "Close")}</Button>
        <Button danger icon={<LogoutOutlined />} onClick={logoutAll}>{L("تسجيل الخروج من كل الأجهزة", "Sign out of every device")}</Button>
      </>}>
      {sessions.error && <Alert type="error" showIcon message={errorText(sessions.error)} className="mb" />}
      <Table<Session> rowKey="id" size="small" loading={sessions.isLoading} dataSource={sessions.data ?? []} pagination={false} scroll={{ x: 640 }}
        columns={[
          { title: L("الجهاز", "Device"), render: (_, s) => <span title={s.userAgent ?? ""}>{device(s.userAgent)} {s.current && <Tag color="green">{L("هذه الجلسة", "This session")}</Tag>}</span> },
          { title: L("العنوان", "Address"), dataIndex: "ip", render: (v: string | null) => <span className="ltr">{v ?? "—"}</span> },
          { title: L("بدأت", "Started"), dataIndex: "createdAt", render: (v: string) => <span className="ltr">{formatDateTime(v)}</span> },
          { title: L("آخر نشاط", "Last active"), dataIndex: "lastSeenAt", render: (v: string) => <span className="ltr">{formatDateTime(v)}</span> },
        ]} />
    </Modal>
  );
}
