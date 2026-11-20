import { useId, useState } from "react";
import { Badge, Button, Popover } from "antd";
import { BellOutlined } from "@ant-design/icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import { api } from "../../api/client";
import { formatDateTime } from "../../shared/format";
import { L } from "../../shared/i18n";
import "./notifications.css";
import { queryKeys } from "../../api/queries";

/** عنصر في صندوق الإشعارات كما يعيده GET /api/notifications. */
export interface InboxItem {
  id: string;
  kind: string;
  title: string;
  body: string;
  link: string | null;
  createdAt: string;
  readAt: string | null;
}

export interface Inbox { items: InboxItem[]; unread: number }

export const INBOX_KEY = queryKeys.notifications;

/**
 * صندوق الإشعارات داخل التطبيق (المطلب 4-15): كل ما أرسل للمستخدم بالبريد يظهر هنا أيضًا، فلا يضيع بفشل البريد.
 * الاستطلاع كل دقيقة سلبي (لا يمد مهلة الخمول). فتح إشعار يعلمه مقروءًا ثم ينتقل إلى رابطه.
 */
export function NotificationBell() {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const titleId = useId();
  const inbox = useQuery({ queryKey: INBOX_KEY, queryFn: () => api.poll<Inbox>("/api/notifications"), refetchInterval: 60_000 });
  const unread = inbox.data?.unread ?? 0;
  const items = inbox.data?.items ?? [];

  const openItem = async (item: InboxItem) => {
    setOpen(false);
    if (!item.readAt) {
      try {
        await api.post(`/api/notifications/${item.id}/read`);
      } finally {
        await qc.invalidateQueries({ queryKey: INBOX_KEY });
      }
    }
    if (item.link?.startsWith("/")) navigate(item.link);
  };

  const readAll = async () => {
    await api.post("/api/notifications/read-all");
    await qc.invalidateQueries({ queryKey: INBOX_KEY });
  };

  const content = (
    <section className="nb-panel" aria-labelledby={titleId}>
      <header className="nb-head">
        <b id={titleId}>{L("الإشعارات", "Notifications")}</b>
        {unread > 0 && <Button type="link" size="small" onClick={() => void readAll()}>{L("تعليم الكل مقروءًا", "Mark all read")}</Button>}
      </header>
      {inbox.isError && !inbox.data ? (
        <p className="nb-empty" role="alert">{L("تعذر تحميل الإشعارات.", "Could not load notifications.")}</p>
      ) : items.length === 0 ? (
        <p className="nb-empty">{inbox.isLoading ? L("جار التحميل…", "Loading…") : L("لا إشعارات بعد.", "No notifications yet.")}</p>
      ) : (
        <ul className="nb-list">
          {items.map((item) => (
            <li key={item.id}>
              <button type="button" className={`nb-item${item.readAt ? "" : " is-unread"}`} onClick={() => void openItem(item)}>
                <span className="nb-title">{!item.readAt && <span className="sr-only">{L("غير مقروء: ", "Unread: ")}</span>}{item.title}</span>
                <span className="nb-body">{item.body}</span>
                <time className="nb-time" dateTime={item.createdAt}>{formatDateTime(item.createdAt)}</time>
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );

  const label = unread > 0 ? L(`الإشعارات، ${unread} غير مقروء`, `Notifications, ${unread} unread`) : L("الإشعارات", "Notifications");
  return (
    <Popover trigger="click" open={open} onOpenChange={setOpen} content={content} placement="bottom" arrow={false}>
      <Button type="text" className="nb-bell" aria-label={label} aria-haspopup="dialog" aria-expanded={open}
        icon={<Badge count={unread} size="small" overflowCount={99}><BellOutlined aria-hidden /></Badge>} />
    </Popover>
  );
}
