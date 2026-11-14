import { Component, useEffect, useId, useLayoutEffect, useRef, useState, type ErrorInfo, type KeyboardEvent, type ReactNode } from "react";
import { Button, Drawer, Grid, Popover, Tooltip } from "antd";
import { CheckOutlined, SettingOutlined, UndoOutlined } from "@ant-design/icons";
import { L, useLang } from "../shared/i18n";
import { useAppearance } from "../styles/design-system/appearance";
import { cornerLabels, cornerStyles, themeLabels, themeNames, type UiLabel } from "../styles/design-system/tokens";
import "./appearance.css";

/**
 * مجموعة اختيار بنمط زر الاختيار (radiogroup): Tab يدخل على المختار وحده، والأسهم تنقل وتختار (معكوسة يمينًا ويسارًا
 * في العربية)، وHome وEnd أول الخيارات وآخرها، والمسافة وEnter تختار. التغيير فوري.
 */
function useRadio<T extends string>(values: readonly T[], value: T, onChange: (next: T) => void) {
  const rtl = useLang() === "ar";
  const refs = useRef<(HTMLButtonElement | null)[]>([]);
  const checked = Math.max(0, values.indexOf(value));
  function onKeyDown(e: KeyboardEvent<HTMLButtonElement>, i: number) {
    const n = values.length;
    const moves: Record<string, number> = {
      ArrowDown: i + 1, ArrowUp: i - 1, ArrowRight: rtl ? i - 1 : i + 1, ArrowLeft: rtl ? i + 1 : i - 1, Home: 0, End: n - 1,
    };
    const to = moves[e.key];
    if (to === undefined) {
      if (e.key === " " || e.key === "Enter") {
        e.preventDefault();
        onChange(values[i]);
      }
      return;
    }
    e.preventDefault();
    const j = (to + n) % n;
    onChange(values[j]);
    refs.current[j]?.focus();
  }
  /** التركيز على الخيار المختار الآن (بعد الرسم: يقرأ aria-checked من الصفحة)؛ true إن نجح (المخفي لا يأخذ التركيز). */
  const focusChecked = () => {
    const el = refs.current.find((b) => b?.getAttribute("aria-checked") === "true");
    el?.focus({ preventScroll: true });
    return !!el && document.activeElement === el;
  };
  const radio = (v: T, i: number) => ({
    ref: (el: HTMLButtonElement | null) => {
      refs.current[i] = el;
    },
    type: "button" as const,
    role: "radio" as const,
    "aria-checked": v === value,
    tabIndex: i === checked ? 0 : -1,
    onClick: () => onChange(v),
    onKeyDown: (e: KeyboardEvent<HTMLButtonElement>) => onKeyDown(e, i),
  });
  return { radio, focusChecked };
}

/**
 * محتوى لوحة المظهر: لوحات الألوان، والزوايا، و«استعادة الافتراضي». الخيارات من سجل السمات (tokens.ts: themes وthemeLabels
 * وcorners وcornerLabels)، فأي لوحة ألوان تضاف هناك تظهر هنا وحدها، وكل معاينة داخل عنصر بسمتها (data-theme) فتعرض ألوانها
 * الحقيقية (رموز السمة والقسم الفاتح فقط، بلا data-section داخلها، فلا تتسرب سمة إلى أخرى).
 */
function PanelBody({ open, noteId, openKey = 0 }: { open: boolean; noteId: string; openKey?: number }) {
  const lang = useLang();
  const { theme, corners, setTheme, setCorners, reset, isDefault } = useAppearance();
  const uid = useId();
  const palettesId = `${uid}-palettes`;
  const cornersId = `${uid}-corners`;
  const { radio: themeRadio, focusChecked } = useRadio(themeNames, theme, setTheme);
  const { radio: cornerRadio } = useRadio(cornerStyles, corners, setCorners);
  const name = (label: UiLabel) => (lang === "ar" ? label.ar : label.en);

  // عند الفتح: التركيز على لوحة الألوان المختارة (داخل الحوار، فيعلن قارئ الشاشة اسمه، والأسهم تعمل فورًا).
  // النافذة تظهر بعد إطار أو أكثر (حركة antd)، فيعاد المحاولة كل إطار حتى تظهر، نحو نصف ثانية على الأكثر.
  // openKey يزيد مع كل فتح: antd يحفظ محتوى النافذة المغلقة كما هو، فلا يرى هذا المكون open يعود من false إلى true.
  useEffect(() => {
    if (!open) return;
    let frame = 0;
    let tries = 0;
    const tick = () => {
      if (focusChecked() || ++tries > 30) return;
      frame = requestAnimationFrame(tick);
    };
    frame = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frame);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- مرة عند كل فتح
  }, [open, openKey]);

  // «استعادة الافتراضي» يتعطل بعد الضغط، فلا يبقى التركيز عليه: ينتقل إلى اللوحة المختارة الآن.
  function restore() {
    reset();
    requestAnimationFrame(focusChecked);
  }

  return (
    <>
      <p id={noteId} className="ap-note">
        {L("يتغير شكل المنظومة فورًا، ويحفظ اختيارك في هذا المتصفح.", "Changes apply right away and are saved in this browser.")}
      </p>

      <div className="ap-group">
        <h3 id={palettesId} className="ap-label">{L("لوحة الألوان", "Colour palette")}</h3>
        <div role="radiogroup" aria-labelledby={palettesId} className="ap-palettes">
          {themeNames.map((n, i) => (
            <button key={n} className="ap-palette" {...themeRadio(n, i)}>
              <span className="ap-preview" data-theme={n} aria-hidden>
                <span className="ap-pv-side">
                  <span />
                  <span />
                  <span />
                </span>
                <span className="ap-pv-main">
                  <span className="ap-pv-band"><span /></span>
                  <span className="ap-pv-card">
                    <span />
                    <span />
                    <span className="ap-pv-btn" />
                  </span>
                </span>
              </span>
              <span className="ap-palette-name">{name(themeLabels[n])}</span>
              {n === theme && <CheckOutlined className="ap-check" aria-hidden />}
            </button>
          ))}
        </div>
      </div>

      <div className="ap-group">
        <h3 id={cornersId} className="ap-label">{L("الزوايا", "Corners")}</h3>
        <div role="radiogroup" aria-labelledby={cornersId} className="ap-seg">
          {cornerStyles.map((c, i) => (
            <button key={c} className="ap-seg-item" {...cornerRadio(c, i)}>
              <span className="ap-corner" data-corners={c} aria-hidden />
              {name(cornerLabels[c])}
            </button>
          ))}
        </div>
      </div>

      <Button block className="ap-restore" icon={<UndoOutlined aria-hidden />} onClick={restore} disabled={isDefault}>
        {L("استعادة الافتراضي", "Restore default")}
      </Button>
    </>
  );
}

/**
 * اللوحة على الحاسوب: نافذة صغيرة (Popover) تحت الزر. حوار غير حاجب بعنوان ووصف؛ Escape يغلقه ويعيد التركيز إلى الزر،
 * والخروج بـ Tab من أوله أو آخره كذلك. النقر خارجه يغلقه.
 */
function PopoverDialog({ open, onClose, openKey }: { open: boolean; onClose: (refocus: boolean) => void; openKey?: number }) {
  const uid = useId();
  const titleId = `${uid}-title`;
  const noteId = `${uid}-note`;
  const ref = useRef<HTMLDivElement>(null);
  // صندوق Popover في antd يحمل role="tooltip"؛ هذا الصندوق حوار فيه أزرار، فيلغى ذلك الدور ويبقى دور الحوار وحده.
  useLayoutEffect(() => {
    ref.current?.closest('[role="tooltip"]')?.setAttribute("role", "presentation");
  });
  function onKeyDown(e: KeyboardEvent<HTMLDivElement>) {
    if (e.key === "Escape") {
      e.preventDefault();
      e.stopPropagation();
      onClose(true);
      return;
    }
    if (e.key !== "Tab" || !ref.current) return;
    const items = [...ref.current.querySelectorAll<HTMLElement>("button:not([disabled])")].filter((el) => el.tabIndex >= 0);
    const edge = e.shiftKey ? items[0] : items[items.length - 1];
    if (document.activeElement === edge) {
      e.preventDefault();
      onClose(true);
    }
  }
  return (
    <div ref={ref} role="dialog" aria-labelledby={titleId} aria-describedby={noteId} className="ap-pop-body" onKeyDown={onKeyDown}>
      <h2 id={titleId} className="ap-title">{L("المظهر", "Appearance")}</h2>
      <PanelBody open={open} noteId={noteId} openKey={openKey} />
    </div>
  );
}

/**
 * زر «المظهر» في الشريط العلوي بجانب زر اللغة وقائمة الحساب، بحجم زر اللغة وشكله، وتلميح باسمه.
 * يفتح اللوحة: نافذة تحت الزر على الحاسوب والجهاز اللوحي، ولوحة سفلية (Drawer) على الجوال (أضيق من md):
 * حوار بعنوان، والتركيز داخله، وEscape يغلقه ويعود التركيز إلى الزر. بلا تعتيم، فيرى المستخدم التغيير خلفها.
 */
function AppearanceButtonInner() {
  const lang = useLang();
  const screens = Grid.useBreakpoint();
  const sheet = screens.md === false;
  const [open, setOpenState] = useState(false);
  const [opens, setOpens] = useState(0);
  const [tip, setTip] = useState(false);
  // كل فتح يزيد العداد، فتنقل اللوحة التركيز إلى الخيار المختار في كل مرة.
  const setOpen = (next: boolean) => {
    if (next) setOpens((n) => n + 1);
    setOpenState(next);
  };
  const button = useRef<HTMLButtonElement | HTMLAnchorElement>(null);
  const uid = useId();
  const sheetTitleId = `${uid}-sheet-title`;
  const sheetNoteId = `${uid}-sheet-note`;
  const label = L("المظهر", "Appearance");

  // تغير العرض بين الحاسوب والجوال وهي مفتوحة: تغلق، فلا تبقى نسختان.
  useEffect(() => setOpenState(false), [sheet]);

  function close(refocus: boolean) {
    setOpen(false);
    if (refocus) button.current?.focus({ preventScroll: true });
  }

  const trigger = (
    <Tooltip title={label} trigger={["hover", "focus"]} mouseEnterDelay={0.15} open={tip && !open} onOpenChange={setTip}>
      <Button
        ref={button}
        type="text"
        className="lcp-lang lcp-look"
        icon={<SettingOutlined aria-hidden />}
        aria-label={label}
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={sheet ? () => setOpen(true) : undefined}
        onKeyDown={(e) => {
          if (e.key === "Escape" && open) {
            e.stopPropagation();
            close(true);
          }
        }}
      />
    </Tooltip>
  );

  if (sheet) {
    return (
      <>
        {trigger}
        <Drawer
          open={open}
          onClose={() => setOpen(false)}
          placement="bottom"
          height="auto"
          rootClassName="ap-sheet"
          autoFocus={false}
          title={<span id={sheetTitleId}>{label}</span>}
          aria-labelledby={sheetTitleId}
          aria-describedby={sheetNoteId}
          closable={{ "aria-label": L("إغلاق", "Close") }}
          styles={{ mask: { background: "transparent" } }}
        >
          <PanelBody open={open} noteId={sheetNoteId} openKey={opens} />
        </Drawer>
      </>
    );
  }

  return (
    <Popover
      open={open}
      onOpenChange={setOpen}
      trigger="click"
      arrow={false}
      placement={lang === "ar" ? "bottomLeft" : "bottomRight"}
      rootClassName="ap-pop"
      content={<PopoverDialog open={open} onClose={close} openKey={opens} />}
    >
      {/* الغلاف يستقبل نقرة الفتح ووصف antd (aria-describedby)، فيبقى اسم الزر «المظهر» وحده. */}
      <span className="lcp-look-anchor">{trigger}</span>
    </Popover>
  );
}

/** خطأ في اللوحة لا يسقط الصفحة: يختفي الزر وحده، ويبقى المظهر المحفوظ مطبقًا (AppearanceProvider مستقل عنها). */
class PanelBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  componentDidCatch(error: unknown, info: ErrorInfo) {
    console.error("Appearance panel failed; the page keeps its current look.", error, info.componentStack);
  }

  render() {
    return this.state.failed ? null : this.props.children;
  }
}

export function AppearanceButton() {
  return (
    <PanelBoundary>
      <AppearanceButtonInner />
    </PanelBoundary>
  );
}
