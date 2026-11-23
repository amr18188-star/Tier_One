import { Form, Input } from "antd";
import { L } from "../../shared/i18n";
import { englishNameRules, nationalIdRules, phoneRules } from "./accountData";

/** ADR-0021: the English name of the account form, required in Latin letters. */
export function EnglishNameItem() {
  return (
    <Form.Item name="fullNameEn" label={L("الاسم بالإنجليزية", "English name")} rules={englishNameRules()}
      extra={L("الاسم كاملًا بحروف لاتينية.", "The full name, in Latin letters.")}>
      <Input dir="ltr" />
    </Form.Item>
  );
}

/**
 * ADR-0021: the national ID and the mobile number of the account form. The server never sends a national ID back in full, so on an
 * edit the field starts empty beside the masked number on file; leaving it empty keeps that number.
 */
export function PersonalDataItems({ nationalIdOnFile }: { nationalIdOnFile?: string | null }) {
  return (
    <>
      <Form.Item name="nationalId" label={L("الرقم القومي", "National ID")} rules={nationalIdRules(!!nationalIdOnFile)}
        extra={nationalIdOnFile
          ? L(`المحفوظ: ${nationalIdOnFile}. اتركه فارغًا ليبقى كما هو، أو اكتب الرقم كاملًا لتغييره.`, `On file: ${nationalIdOnFile}. Leave empty to keep it, or type the full number to change it.`)
          : L("14 رقمًا كما في بطاقة الرقم القومي. بعد الحفظ لا يظهر منه إلا آخر أربعة أرقام.", "The 14 digits on the national ID card. After saving, only its last four digits are shown.")}>
        <Input dir="ltr" inputMode="numeric" autoComplete="off" maxLength={20} />
      </Form.Item>
      <Form.Item name="phone" label={L("رقم التليفون المحمول", "Mobile number")} rules={phoneRules()}
        extra={L("11 رقمًا يبدأ بـ 010 أو 011 أو 012 أو 015.", "11 digits starting with 010, 011, 012 or 015.")}>
        <Input dir="ltr" inputMode="tel" autoComplete="off" maxLength={20} />
      </Form.Item>
    </>
  );
}
