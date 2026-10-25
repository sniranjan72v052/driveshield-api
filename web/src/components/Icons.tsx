import type { IncidentType } from "../api/types";

/** Simple line glyphs for the four incident types. Drawn for this app, 40x40 grid. */
export function IncidentIcon({ type }: { type: IncidentType }) {
  const common = {
    width: 40,
    height: 40,
    viewBox: "0 0 40 40",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: 2.2,
    strokeLinecap: "round" as const,
    strokeLinejoin: "round" as const,
    "aria-hidden": true,
  };

  switch (type) {
    case "Accident":
      return (
        <svg {...common}>
          <path d="M5 28v-6l3-6h14l3 6v6z" />
          <circle cx="11" cy="29" r="2.6" />
          <circle cx="21" cy="29" r="2.6" />
          <path d="M29 9l2.5 4M34 14l-4 1.5M30 5v3" />
        </svg>
      );
    case "Breakdown":
      return (
        <svg {...common}>
          <path d="M20 6L35 32H5z" />
          <path d="M20 16v8" />
          <circle cx="20" cy="28" r="0.8" fill="currentColor" />
        </svg>
      );
    case "NaturalCalamity":
      return (
        <svg {...common}>
          <path d="M8 17v-4l3-5h9l3 5v4z" />
          <path d="M4 25q4-4 8 0t8 0 8 0 8 0" />
          <path d="M4 32q4-4 8 0t8 0 8 0 8 0" />
        </svg>
      );
    case "WrongParking":
      return (
        <svg {...common}>
          <circle cx="20" cy="20" r="14" />
          <path d="M15 28V12h6a5 5 0 010 10h-6" />
          <path d="M9 31L31 9" />
        </svg>
      );
  }
}

const glyph = {
  fill: "none",
  stroke: "currentColor",
  strokeWidth: 2.2,
  strokeLinecap: "round" as const,
  strokeLinejoin: "round" as const,
  "aria-hidden": true,
};

/** The DriveShield mark: a shield with a tick. */
export function ShieldMark({ size = 28 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 32 32" {...glyph} strokeWidth={2.2}>
      <path d="M16 3.5l10 4v7.5c0 6.8-4.2 11.4-10 13.5C10.2 26.4 6 21.8 6 15V7.5z" />
      <path d="M11.5 15.8l3.300 3.300 6-6.400" />
    </svg>
  );
}

/** A small tick inside a shield, for the "what we promise" lines. */
export function ShieldTick({ size = 20 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" {...glyph} strokeWidth={2}>
      <path d="M12 3l7.500 3v5.500c0 5-3.200 8.4-7.500 10C7.700 19.9 4.500 16.5 4.500 11.5V6z" />
      <path d="M8.8 12l2.400 2.400 4.200-4.600" />
    </svg>
  );
}

/** A map pin. */
export function PinGlyph({ size = 30 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" {...glyph} strokeWidth={2}>
      <path d="M12 21s-6.500-5.800-6.500-11a6.500 6.500 0 0113 0c0 5.200-6.500 11-6.500 11z" />
      <circle cx="12" cy="10" r="2.400" />
    </svg>
  );
}

/** A car seen from the front, for the home page picture. */
export function CarGlyph() {
  return (
    <svg viewBox="0 0 120 100" fill="none" stroke="currentColor" strokeWidth="6" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      <path d="M26 54l8.500-23c1.600-4.200 5-6 9.500-6h32c4.500 0 7.900 1.800 9.500 6L94 54" />
      <rect x="14" y="52" width="92" height="30" rx="13" />
      <circle cx="36" cy="67" r="3.500" fill="currentColor" />
      <circle cx="84" cy="67" r="3.500" fill="currentColor" />
      <path d="M32 82v8M88 82v8M20 38l-9-6M100 38l9-6" />
    </svg>
  );
}

/** A plain tick, for lists of services. */
export function TickGlyph({ size = 20 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" {...glyph} strokeWidth={2.4}>
      <circle cx="12" cy="12" r="9" />
      <path d="M8 12.3l2.800 2.800 5.200-5.600" />
    </svg>
  );
}
