import { useState } from "react";
import { Alert, Button, Form, Input, Modal, Typography } from "antd";
import { LockOutlined, SafetyOutlined } from "@ant-design/icons";
import { api } from "../api/client";
import { errorText } from "./format";
import { L } from "./i18n";
import { useSession, type ResumeResult } from "./session";

/**
 * UX-1/FE-3: the session ended while the user was working. Signing in again happens here, over the page, so values typed
 * and not yet saved stay on screen and are saved once the session is back. Only the same account can continue the page;
 * signing in as someone else reloads the application.
 */
export function SessionExpiredDialog() {
  const { me, expired, resume } = useSession();
  const [step, setStep] = useState<"password" | "code">("password");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  if (!expired || !me) return null;

  const after = (r: ResumeResult) => {
    if (r === "reload") window.location.replace("/");
    else if (r === "code") setStep("code");
    else setStep("password");
  };
  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setError(null);
    try {
      await action();
      after(await resume());
    } catch (e) {
      setError(errorText(e));
    } finally {
      setBusy(false);
    }
  }
  async function switchAccount() {
    try {
      await api.post("/api/auth/logout");
    } finally {
      window.location.replace("/login");
    }
  }

  return (
    <Modal open closable={false} maskClosable={false} keyboard={false} footer={null} destroyOnHidden
      title={L("انتهت جلستك", "Your session has ended")}>
      <Typography.Paragraph>
        {L("سجّل الدخول من جديد لتكمل من حيث توقفت. ما كتبته ولم يحفظ باقٍ في الصفحة، ويحفظ بعد الدخول.",
          "Sign in again to continue where you left off. What you typed and was not saved is still on the page and is saved once you are back.")}
      </Typography.Paragraph>
      {error && <Alert type="error" showIcon message={error} className="mb" role="alert" />}
      {step === "password" ? (
        <Form layout="vertical" requiredMark={false} onFinish={(v: { password: string }) => run(() => api.post("/api/auth/login", { email: me.email, password: v.password }))}>
          <Form.Item label={L("البريد الإلكتروني", "Email")}>
            <Input value={me.email} disabled dir="ltr" autoComplete="username" />
          </Form.Item>
          <Form.Item name="password" label={L("كلمة المرور", "Password")} rules={[{ required: true, message: L("اكتب كلمة المرور", "Enter your password") }]}>
            <Input.Password prefix={<LockOutlined />} autoComplete="current-password" dir="ltr" autoFocus />
          </Form.Item>
          <Button type="primary" htmlType="submit" block loading={busy}>{L("تسجيل الدخول والمتابعة", "Sign in and continue")}</Button>
        </Form>
      ) : (
        <Form layout="vertical" requiredMark={false} onFinish={(v: { code: string }) => run(() => api.post("/api/auth/mfa/verify", v))}>
          <Form.Item name="code" label={L("رمز تطبيق المصادقة", "Authenticator code")} rules={[{ required: true, pattern: /^\d{6}$/, message: L("ستة أرقام", "Six digits") }]}>
            <Input prefix={<SafetyOutlined />} inputMode="numeric" autoComplete="one-time-code" maxLength={6} dir="ltr" autoFocus />
          </Form.Item>
          <Button type="primary" htmlType="submit" block loading={busy}>{L("تحقق والمتابعة", "Verify and continue")}</Button>
        </Form>
      )}
      <Button type="link" block className="mt" onClick={() => void switchAccount()}>
        {L("الدخول بحساب آخر (تضيع القيم غير المحفوظة)", "Sign in as someone else (unsaved values are lost)")}
      </Button>
    </Modal>
  );
}
