import { useState } from "react";
import { Alert, Button, Form, Image, Input, Typography } from "antd";
import { LockOutlined, MailOutlined, SafetyOutlined } from "@ant-design/icons";
import { api } from "../../api/client";
import { AuthLayout } from "../../shared/AuthLayout";
import { errorText } from "../../shared/format";
import { L } from "../../shared/i18n";
import { useReloadSession, useSession } from "../../shared/session";

/**
 * الدخول على خطوات تحددها حالة الجلسة: كلمة المرور، ثم تغيير الكلمة المؤقتة إن لزم،
 * ثم التحقق بخطوتين (أو تفعيله أول مرة).
 */
export function LoginPage() {
  const { me } = useSession();
  // من فعّل التحقق بخطوتين يتحقق أولًا، ثم يغير الكلمة المؤقتة إن لزم، ثم يفعّل التحقق من لم يفعله.
  const step = !me ? "password"
    : me.mfaPending && me.mfaEnabled ? "verify"
    : me.mustChangePassword ? "change"
    : me.mfaPending ? "enroll" : "password";
  return (
    <AuthLayout>
      {step === "password" && <PasswordStep />}
      {step === "change" && <ChangePasswordStep />}
      {step === "verify" && <CodeStep />}
      {step === "enroll" && <EnrollStep />}
    </AuthLayout>
  );
}

function PasswordStep() {
  const reload = useReloadSession();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  async function submit(v: { email: string; password: string }) {
    setBusy(true);
    setError(null);
    try {
      await api.post("/api/auth/login", v);
      await reload();
    } catch (e) {
      setError(errorText(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <>
      <Typography.Title level={3}>{L("مرحبًا بك", "Welcome")}</Typography.Title>
      <Typography.Paragraph type="secondary">{L("سجّل دخولك بحساب الجامعة للمتابعة.", "Sign in with your university account to continue.")}</Typography.Paragraph>
      {error && <Alert type="error" showIcon message={error} className="mb" />}
      <Form layout="vertical" onFinish={submit} requiredMark={false}>
        <Form.Item name="email" label={L("البريد الإلكتروني", "Email")} rules={[{ required: true, message: L("اكتب البريد", "Enter your email") }]}>
          <Input prefix={<MailOutlined />} autoComplete="username" dir="ltr" size="large" />
        </Form.Item>
        <Form.Item name="password" label={L("كلمة المرور", "Password")} rules={[{ required: true, message: L("اكتب كلمة المرور", "Enter your password") }]}>
          <Input.Password prefix={<LockOutlined />} autoComplete="current-password" dir="ltr" size="large" />
        </Form.Item>
        <Button type="primary" htmlType="submit" block size="large" loading={busy}>{L("تسجيل الدخول", "Sign in")}</Button>
      </Form>
    </>
  );
}

function CodeStep() {
  const reload = useReloadSession();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  async function submit(v: { code: string }) {
    setBusy(true);
    setError(null);
    try {
      await api.post("/api/auth/mfa/verify", v);
      await reload();
    } catch (e) {
      setError(errorText(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <>
      <Typography.Title level={3}>{L("التحقق بخطوتين", "Two-step verification")}</Typography.Title>
      <Typography.Paragraph type="secondary">{L("اكتب الرمز المكون من 6 أرقام من تطبيق المصادقة.", "Enter the 6-digit code from your authenticator app.")}</Typography.Paragraph>
      {error && <Alert type="error" showIcon message={error} className="mb" />}
      <Form layout="vertical" onFinish={submit} requiredMark={false}>
        <Form.Item name="code" label={L("رمز تطبيق المصادقة", "Authenticator code")} rules={[{ required: true, pattern: /^\d{6}$/, message: L("ستة أرقام", "Six digits") }]}>
          <Input prefix={<SafetyOutlined />} inputMode="numeric" autoComplete="one-time-code" maxLength={6} dir="ltr" size="large" className="code-input" />
        </Form.Item>
        <Button type="primary" htmlType="submit" block size="large" loading={busy}>{L("تحقق", "Verify")}</Button>
      </Form>
      <SignOutLink />
    </>
  );
}

function EnrollStep() {
  const reload = useReloadSession();
  const [setup, setSetup] = useState<{ secret: string; qrDataUri: string } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  async function start() {
    setError(null);
    try {
      setSetup(await api.post("/api/auth/mfa/setup"));
    } catch (e) {
      setError(errorText(e));
    }
  }
  async function enable(v: { code: string }) {
    setBusy(true);
    setError(null);
    try {
      await api.post("/api/auth/mfa/enable", v);
      await reload();
    } catch (e) {
      setError(errorText(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <>
      <Typography.Title level={3}>{L("تفعيل التحقق بخطوتين", "Set up two-step verification")}</Typography.Title>
      <Typography.Paragraph type="secondary">{L("دورك يتطلب التحقق بخطوتين. امسح الرمز بتطبيق مصادقة ثم اكتب الرمز الذي يظهر.", "Your role requires two-step verification. Scan the code with an authenticator app, then enter the code it shows.")}</Typography.Paragraph>
      {error && <Alert type="error" showIcon message={error} className="mb" />}
      {!setup ? (
        <Button type="primary" block size="large" onClick={start}>{L("ابدأ الإعداد", "Start setup")}</Button>
      ) : (
        <>
          <div className="qr-box"><Image src={setup.qrDataUri} alt={L("رمز QR للإعداد", "Setup QR code")} preview={false} width={180} /></div>
          <Typography.Paragraph copyable={{ text: setup.secret }} className="secret">{setup.secret}</Typography.Paragraph>
          <Form layout="vertical" onFinish={enable} requiredMark={false}>
            <Form.Item name="code" label={L("رمز تطبيق المصادقة", "Authenticator code")} rules={[{ required: true, pattern: /^\d{6}$/, message: L("ستة أرقام", "Six digits") }]}>
              <Input inputMode="numeric" maxLength={6} dir="ltr" size="large" className="code-input" />
            </Form.Item>
            <Button type="primary" htmlType="submit" block size="large" loading={busy}>{L("فعّل وادخل", "Enable and continue")}</Button>
          </Form>
        </>
      )}
      <SignOutLink />
    </>
  );
}

function ChangePasswordStep() {
  const reload = useReloadSession();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  async function submit(v: { currentPassword: string; newPassword: string }) {
    setBusy(true);
    setError(null);
    try {
      await api.post("/api/auth/change-password", { currentPassword: v.currentPassword, newPassword: v.newPassword });
      await reload();
    } catch (e) {
      setError(errorText(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <>
      <Typography.Title level={3}>{L("غيّر كلمة المرور المؤقتة", "Change your temporary password")}</Typography.Title>
      <Typography.Paragraph type="secondary">{L("12 حرفًا على الأقل، ولا تتضمن بريدك.", "At least 12 characters, without your email.")}</Typography.Paragraph>
      {error && <Alert type="error" showIcon message={error} className="mb" />}
      <Form layout="vertical" onFinish={submit} requiredMark={false}>
        <Form.Item name="currentPassword" label={L("كلمة المرور المؤقتة", "Temporary password")} rules={[{ required: true }]}>
          <Input.Password autoComplete="current-password" dir="ltr" />
        </Form.Item>
        <Form.Item name="newPassword" label={L("كلمة المرور الجديدة", "New password")} rules={[{ required: true, min: 12, message: L("12 حرفًا على الأقل", "At least 12 characters") }]}>
          <Input.Password autoComplete="new-password" dir="ltr" />
        </Form.Item>
        <Form.Item
          name="confirm"
          label={L("تأكيد كلمة المرور", "Confirm password")}
          dependencies={["newPassword"]}
          rules={[{ required: true }, ({ getFieldValue }) => ({ validator: (_, v) => (v === getFieldValue("newPassword") ? Promise.resolve() : Promise.reject(new Error(L("غير متطابقة", "Does not match")))) })]}
        >
          <Input.Password autoComplete="new-password" dir="ltr" />
        </Form.Item>
        <Button type="primary" htmlType="submit" block loading={busy}>{L("احفظ", "Save")}</Button>
      </Form>
      <SignOutLink />
    </>
  );
}

function SignOutLink() {
  const reload = useReloadSession();
  return (
    <Button type="link" block className="mt" onClick={async () => { await api.post("/api/auth/logout"); await reload(); }}>
      {L("الدخول بحساب آخر", "Use another account")}
    </Button>
  );
}
