interface SectionHeadingProps {
  eyebrow: string;
  title: string;
  highlight?: string;
  description?: string;
  align?: "center" | "right";
}

/**
 * Eyebrow, title with a highlighted part, and an optional description.
 * Colours come from the section's scope (data-section="dark" on dark sections), so there is no `dark` prop.
 */
export function SectionHeading({ eyebrow, title, highlight, description, align = "center" }: SectionHeadingProps) {
  return (
    <div className={`mb-12 max-w-3xl ${align === "center" ? "mx-auto text-center" : "text-right"}`}>
      <div className="mb-4 inline-flex items-center gap-3 text-xs font-bold tracking-wider text-ds-eyebrow">
        <span className="h-px w-8 bg-current" />
        {eyebrow}
        <span className="h-px w-8 bg-current" />
      </div>

      <h2 className="text-3xl font-bold leading-[1.5] tracking-tight sm:text-4xl lg:text-5xl text-ds-heading">
        {title}{" "}
        {highlight && <span className="text-ds-highlight">{highlight}</span>}
      </h2>

      {description && <p className="mt-5 text-sm leading-8 sm:text-base text-ds-body">{description}</p>}
    </div>
  );
}
