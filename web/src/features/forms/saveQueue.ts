/**
 * طابور الحفظ لحقل واحد، بلا React (يختبر وحده في saveQueue.test.ts):
 * - طلب واحد في الطريق؛ ما يطلب أثناءه يرسل بعده مرة واحدة بآخر قيمة وبالإصدار العائد من الطلب السابق.
 * - لا طلب إن كانت القيمة كآخر قيمة محفوظة أو غير جاهزة (ready).
 * - الرد بعد invalidate() (إعادة الضبط) قديم: يتجاهل برقم التسلسل.
 * - الخطأ يوقف الطابور حتى run() جديد (إعادة المحاولة أو تغيير جديد).
 */
export type QueueState = "idle" | "saving" | "saved" | "error";

export interface QueueHooks<D, R> {
  send: (draft: D, version: number | null) => Promise<R>;
  versionOf: (response: R) => number | null;
  same: (a: D, b: D) => boolean;
  ready: (draft: D, saved: D) => boolean;
  onSaved?: (response: R, draft: D) => void;
  onState?: (state: QueueState, error?: unknown) => void;
  /** بعد كل تغير في saving أو unsaved. */
  onBusy?: () => void;
}

export class SaveQueue<D, R> {
  latest: D;
  saved: D;
  version: number | null;
  private running: Promise<void> | null = null;
  private again = false;
  private seq = 0;

  constructor(initial: D, version: number | null, private hooks: QueueHooks<D, R>) {
    this.latest = initial;
    this.saved = initial;
    this.version = version;
  }

  get saving() { return this.running !== null; }
  /** تغيير جاهز للحفظ لم يحفظ بعد. */
  get unsaved() { return !this.hooks.same(this.latest, this.saved) && this.hooks.ready(this.latest, this.saved); }
  get dirty() { return !this.hooks.same(this.latest, this.saved); }

  set(draft: D) {
    this.latest = draft;
    this.hooks.onBusy?.();
  }

  /** يبدأ الحفظ، أو يطلب دورة أخرى بعد الطلب الجاري. يرجع وعد الدورة الجارية. */
  run(): Promise<void> {
    if (this.running) {
      this.again = true;
      return this.running;
    }
    this.again = true;
    const p = this.loop().finally(() => {
      this.running = null;
      this.hooks.onBusy?.();
      // طلب وصل بعد آخر فحص وقبل الانتهاء: دورة جديدة الآن.
      if (this.again) void this.run();
    });
    this.running = p;
    this.hooks.onBusy?.();
    return p;
  }

  /** يحفظ كل ما بقي وينتظره حتى النهاية. */
  async flush(): Promise<void> {
    await this.run();
    while (this.running) await this.running;
  }

  /** يتجاهل رد الطلب الجاري (بعد إعادة الضبط). */
  invalidate() {
    this.seq++;
  }

  /** قيمة الخادم الأحدث تصبح المحفوظة؛ والمسودة معها إن لم يكن فيها تعديل. يرجع false إن بقي التعديل أو كانت أقدم. */
  adopt(server: D, version: number | null): boolean {
    if (this.running) return false;
    if (version !== null && this.version !== null && version < this.version) return false;
    if (this.dirty) return false;
    this.version = version;
    this.saved = server;
    this.latest = server;
    return true;
  }

  private async loop() {
    while (this.again) {
      this.again = false;
      const d = this.latest;
      if (this.hooks.same(d, this.saved) || !this.hooks.ready(d, this.saved)) break;
      const my = ++this.seq;
      this.hooks.onState?.("saving");
      try {
        const r = await this.hooks.send(d, this.version);
        if (my !== this.seq) break;
        this.version = this.hooks.versionOf(r);
        this.saved = d;
        this.hooks.onSaved?.(r, d);
        if (!this.again) this.hooks.onState?.(this.hooks.same(this.latest, d) ? "saved" : "idle");
      } catch (e) {
        if (my !== this.seq) break;
        this.again = false;
        this.hooks.onState?.("error", e);
        break;
      }
    }
  }
}
