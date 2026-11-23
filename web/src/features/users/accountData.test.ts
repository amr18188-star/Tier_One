import { describe, expect, it } from "vitest";
import { digitsOf, isEnglishName, isNationalId, missingLabels, mobileOf } from "./accountData";

/** ADR-0021: the account form checks its data as the server does. The numbers here are made up; none is a real person's. */
describe("account data", () => {
  const today = new Date(2026, 10, 23);

  it("reads digits typed in any form", () => {
    expect(digitsOf(" ٢٨٥٠٥١٥-٠١١٢٣٤٥ ")).toBe("28505150112345");
    expect(digitsOf("+20 10 1234 5678")).toBe("201012345678");
  });

  it("accepts a national ID of 14 digits with a real birth date and a governorate code", () => {
    expect(isNationalId("28505150112345", today)).toBe(true);
    expect(isNationalId("٢٨٥٠٥١٥٠١١٢٣٤٥", today)).toBe(true);
    expect(isNationalId("30002298800017", today)).toBe(true);
  });

  it("refuses a national ID with a wrong length, century, date or governorate, or a birth date after today", () => {
    for (const id of ["2850515011234", "285051501123456", "18505150112345", "28513150112345", "30102298800017", "28505150512345", "32612010112345"])
      expect(isNationalId(id, today), id).toBe(false);
  });

  it("keeps an Egyptian mobile number in one form however it is typed", () => {
    expect(mobileOf("0111 234 5678")).toBe("01112345678");
    expect(mobileOf("+20 12 1234 5678")).toBe("01212345678");
    expect(mobileOf("00201512345678")).toBe("01512345678");
    expect(mobileOf("٠١٠١٢٣٤٥٦٧٨")).toBe("01012345678");
  });

  it("refuses a landline, another prefix or a wrong length", () => {
    for (const n of ["0225551234", "01312345678", "0101234567", "010123456789", ""]) expect(mobileOf(n), n).toBeNull();
  });

  it("takes an English name in Latin letters only", () => {
    expect(isEnglishName("Mona Elsayed Mahmoud")).toBe(true);
    expect(isEnglishName("منى السيد")).toBe(false);
    expect(isEnglishName("Mona السيد")).toBe(false);
  });

  it("names what an account lacks", () => {
    expect(missingLabels(["nationalId", "phone"])).toEqual(["الرقم القومي", "رقم المحمول"]);
    expect(missingLabels([])).toEqual([]);
  });
});
