import { useEffect, useState } from "react";
import { Button, Modal, Result } from "antd";
import { StopOutlined } from "@ant-design/icons";
import { api, onAddressRefused } from "../api/client";
import { L } from "./i18n";

/**
 * ADR-0016: the server refuses this session's requests because of the network address they come from (IP allowlists). The
 * page cannot work from here, so it says why and what to do instead of failing screen by screen. Retrying reloads the page
 * (it works again from an allowed network); signing out is always possible.
 */
export function AddressRefusedDialog() {
  const [message, setMessage] = useState<string | null>(null);
  useEffect(() => {
    onAddressRefused((m) => setMessage(m || L("لا يسمح بالدخول إلى المنظومة من عنوانك الحالي.", "Access is not allowed from your current address.")));
    return () => onAddressRefused(null);
  }, []);
  if (message === null) return null;
  return (
    <Modal open closable={false} maskClosable={false} keyboard={false} footer={null} centered>
      <Result status="warning" icon={<StopOutlined aria-hidden />} title={L("العنوان غير مسموح", "Address not allowed")} subTitle={message}
        extra={[
          <Button key="retry" type="primary" onClick={() => window.location.reload()}>{L("أعد المحاولة", "Try again")}</Button>,
          <Button key="out" onClick={async () => {
            try {
              await api.post("/api/auth/logout");
            } finally {
              window.location.replace("/login");
            }
          }}>{L("تسجيل الخروج", "Sign out")}</Button>,
        ]} />
    </Modal>
  );
}
