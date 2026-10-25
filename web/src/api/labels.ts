import type { CaseStatus, IncidentType, ServiceCategory } from "./types";

export const INCIDENTS: { type: IncidentType; label: string; hint: string }[] = [
  { type: "Accident", label: "Accident", hint: "Collision or damage" },
  { type: "Breakdown", label: "Breakdown", hint: "Won't start or stuck" },
  { type: "NaturalCalamity", label: "Flood or storm", hint: "Water, fallen trees, hail" },
  { type: "WrongParking", label: "Wrong parking", hint: "Towed or fined" },
];

export const INCIDENT_LABEL: Record<IncidentType, string> = {
  Accident: "Accident",
  Breakdown: "Breakdown",
  NaturalCalamity: "Flood or storm",
  WrongParking: "Wrong parking",
};

export const CATEGORY_LABEL: Record<ServiceCategory, string> = {
  InsuranceClaimHelp: "Insurance claim help",
  TowingOnSite: "Towing and on-site support",
  RoadsideAssistance: "Roadside assistance",
  LegalSupport: "Legal support",
  WorkshopGuidance: "Workshop guidance",
  EvVehicleAdvice: "EV vehicle advice",
  WarrantyAssistance: "Warranty assistance",
};

export const ALL_CATEGORIES = Object.keys(CATEGORY_LABEL) as ServiceCategory[];

/** The route a case follows, in order. Cancelled and Escalated are shown as banners instead. */
export const STEPS: { status: CaseStatus; label: string }[] = [
  { status: "Reported", label: "Reported" },
  { status: "Assessed", label: "Case assessed" },
  { status: "ProviderMatched", label: "Finding a provider" },
  { status: "ProviderAccepted", label: "Provider accepted" },
  { status: "EnRoute", label: "Provider on the way" },
  { status: "InProgress", label: "Work in progress" },
  { status: "Resolved", label: "Resolved" },
  { status: "Reviewed", label: "Reviewed" },
];

export const STATUS_LABEL: Record<CaseStatus, string> = {
  Reported: "Reported",
  Assessed: "Case assessed",
  ProviderMatched: "Finding a provider",
  ProviderAccepted: "Provider accepted",
  EnRoute: "Provider on the way",
  InProgress: "Work in progress",
  Resolved: "Resolved",
  Reviewed: "Reviewed",
  Cancelled: "Cancelled",
  Escalated: "Needs our team",
};

/** Button text for each status a provider or customer can move a case to. */
export const ACTION_LABEL: Partial<Record<CaseStatus, string>> = {
  EnRoute: "I'm on my way",
  InProgress: "Start the work",
  Resolved: "Mark as resolved",
  Escalated: "I can't complete this",
  Cancelled: "Cancel request",
};

export const REVIEW_TAGS = ["On time", "Professional", "Fair price", "Friendly", "Clear advice"];

export function formatTime(iso: string): string {
  return new Date(iso).toLocaleString([], {
    day: "numeric",
    month: "short",
    hour: "numeric",
    minute: "2-digit",
  });
}

export function formatKm(meters: number): string {
  return `${(meters / 1000).toFixed(1)} km`;
}
