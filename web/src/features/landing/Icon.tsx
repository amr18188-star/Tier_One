import type { ReactNode } from "react";

/** Line icons of the landing page (24×24 grid, 1.6 stroke), drawn in the current text colour. */
const icons = {
  strategy: (
    <>
      <path d="M12 3v18" />
      <path d="M5 7h14" />
      <path d="M7 7l-3 6h6L7 7Z" />
      <path d="M17 7l-3 6h6l-3-6Z" />
      <path d="M8 21h8" />
    </>
  ),

  users: (
    <>
      <path d="M16 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" />
      <circle cx="10" cy="7" r="4" />
      <path d="M20 21v-2a4 4 0 0 0-3-3.87" />
      <path d="M16 3.13a4 4 0 0 1 0 7.75" />
    </>
  ),

  finance: (
    <>
      <rect x="3" y="5" width="18" height="14" rx="0" />
      <path d="M3 10h18" />
      <path d="M7 15h3" />
      <path d="M16 15h1" />
    </>
  ),

  operations: (
    <>
      <path d="M12 8v4l3 2" />
      <circle cx="12" cy="12" r="9" />
      <path d="M3 3v5h5" />
      <path d="M3 8a9 9 0 0 1 15-4" />
    </>
  ),

  innovation: (
    <>
      <path d="M9 18h6" />
      <path d="M10 22h4" />
      <path d="M15 14.5a7 7 0 1 0-6 0c.7.4 1 1 1 1.5h4c0-.5.3-1.1 1-1.5Z" />
      <path d="M12 3v2" />
      <path d="m4.9 5.9 1.4 1.4" />
      <path d="M19.1 5.9 17.7 7.3" />
    </>
  ),

  leadership: (
    <>
      <circle cx="12" cy="8" r="3" />
      <path d="M5 21v-2a7 7 0 0 1 14 0v2" />
      <path d="m16 4 2 2 3-3" />
    </>
  ),

  chart: (
    <>
      <path d="M3 3v18h18" />
      <path d="m7 14 4-4 4 3 6-7" />
      <path d="M16 6h5v5" />
    </>
  ),

  target: (
    <>
      <circle cx="12" cy="12" r="9" />
      <circle cx="12" cy="12" r="5" />
      <circle cx="12" cy="12" r="1" />
    </>
  ),

  layers: (
    <>
      <path d="m12 3 9 5-9 5-9-5 9-5Z" />
      <path d="m3 12 9 5 9-5" />
      <path d="m3 16 9 5 9-5" />
    </>
  ),

  globe: (
    <>
      <circle cx="12" cy="12" r="10" />
      <path d="M2 12h20" />
      <path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10Z" />
    </>
  ),

  shield: (
    <>
      <path d="M12 22s8-4 8-11V5l-8-3-8 3v6c0 7 8 11 8 11Z" />
      <path d="m9 12 2 2 4-4" />
    </>
  ),

  arrow: (
    <>
      <path d="M19 12H5" />
      <path d="m12 19-7-7 7-7" />
    </>
  ),

  check: (
    <>
      <path d="m5 12 4 4L19 6" />
    </>
  ),

  calendar: (
    <>
      <rect x="3" y="5" width="18" height="16" />
      <path d="M16 3v4M8 3v4M3 11h18" />
    </>
  ),

  building: (
    <>
      <rect x="4" y="3" width="16" height="18" />
      <path d="M9 7h2M13 7h2M9 11h2M13 11h2M9 15h2M13 15h2M10 21v-3h4v3" />
    </>
  ),

  database: (
    <>
      <ellipse cx="12" cy="5" rx="9" ry="3" />
      <path d="M3 5v14c0 1.7 4 3 9 3s9-1.3 9-3V5" />
      <path d="M3 12c0 1.7 4 3 9 3s9-1.3 9-3" />
    </>
  ),
} satisfies Record<string, ReactNode>;

export type IconName = keyof typeof icons;

interface IconProps {
  name: IconName;
  size?: number;
  className?: string;
}

export function Icon({ name, size = 24, className = "" }: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
    >
      {icons[name]}
    </svg>
  );
}
