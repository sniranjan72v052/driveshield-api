export type IncidentType = "Accident" | "Breakdown" | "NaturalCalamity" | "WrongParking";

export type ServiceCategory =
  | "InsuranceClaimHelp"
  | "TowingOnSite"
  | "RoadsideAssistance"
  | "LegalSupport"
  | "WorkshopGuidance"
  | "EvVehicleAdvice"
  | "WarrantyAssistance";

export type CaseStatus =
  | "Reported"
  | "Assessed"
  | "ProviderMatched"
  | "ProviderAccepted"
  | "EnRoute"
  | "InProgress"
  | "Resolved"
  | "Reviewed"
  | "Cancelled"
  | "Escalated";

export interface Me {
  id: string;
  displayName: string;
  role: string; // "Customer" | "Provider" | "Admin"
  providerId: string | null;
  /** The verified email used to sign in, if the person has one. */
  email: string | null;
  /** The verified mobile number used to sign in, if the person has one. */
  loginPhone: string | null;
  /** How many vehicles the person has saved. Zero means they have not been asked yet. */
  vehicleCount: number;
}

/** A customer's vehicle. */
export interface Vehicle {
  id: string;
  make: string;
  model: string;
  registrationNumber: string;
  isElectric: boolean;
  insurerName: string | null;
  policyNumber: string | null;
}

export type VehicleInput = Omit<Vehicle, "id">;

/** Answer to "send me a code". devCode only ever appears on a developer machine. */
export interface OtpStarted {
  challengeId: string;
  channel: "Email" | "Phone";
  destinationMasked: string;
  expiresInSeconds: number;
  resendAfterSeconds: number;
  devCode: string | null;
}

/** Answer to a correct code or a token refresh. */
export interface AuthResponse {
  accessToken: string;
  /** Seconds until the access token runs out. */
  expiresIn: number;
  refreshToken: string;
  user: Me;
}

export interface TimelineEntry {
  id: string;
  status: CaseStatus;
  note: string | null;
  actor: string;
  at: string;
}

export interface ProviderOnCase {
  providerId: string;
  businessName: string;
  phone: string | null;
  category: ServiceCategory;
  offerStatus: string;
  averageRating: number;
  reviewCount: number;
  reviewed: boolean;
}

/** Who took a post or photo down: its author, or the support team. */
export type RemovedBy = "Author" | "Support";

export interface CasePhoto {
  id: string;
  contentType: string;
  /** Set when the photo is gone. Only a placeholder is shown then. */
  removed: RemovedBy | null;
  /** The admin's reason. Only sent to admins. */
  removalReason: string | null;
  /** True when the signed-in person may delete or remove this photo. */
  canRemove: boolean;
}

/** A note and/or photos added to one step of the case. */
export interface CaseComment {
  id: string;
  step: CaseStatus;
  actor: string;
  authorLabel: string; // "Customer", the provider's business name, or "Support team"
  body: string | null;
  at: string;
  photos: CasePhoto[];
  /** Set when the author changed the text. */
  editedAt: string | null;
  /** Set when the whole post is gone. It stays in the thread as a placeholder. */
  removed: RemovedBy | null;
  removalReason: string | null;
  /** The signed-in person wrote this. */
  mine: boolean;
  canEdit: boolean;
  canDelete: boolean;
}

/** One photo in the admin review queue. */
export interface AdminPhoto {
  photoId: string;
  caseId: string;
  incidentType: IncidentType;
  step: CaseStatus;
  actor: string;
  at: string;
}

export interface AdminPhotoPage {
  items: AdminPhoto[];
  /** Pass as ?before= to get the next, older page. Null on the last page. */
  nextBefore: string | null;
}

export interface CaseDetail {
  id: string;
  status: CaseStatus;
  incidentType: IncidentType;
  description: string | null;
  address: string | null;
  latitude: number;
  longitude: number;
  locationIsApproximate: boolean;
  createdAt: string;
  resolvedAt: string | null;
  allowedNextStatuses: CaseStatus[];
  providers: ProviderOnCase[];
  timeline: TimelineEntry[];
  canComment: boolean;
  comments: CaseComment[];
  /** Shown to the customer, support, and providers who took the request. */
  vehicle: Vehicle | null;
}

export interface CaseSummary {
  id: string;
  status: CaseStatus;
  incidentType: IncidentType;
  createdAt: string;
}

export interface Offer {
  offerId: string;
  caseId: string;
  category: ServiceCategory;
  incidentType: IncidentType;
  description: string | null;
  approxLatitude: number;
  approxLongitude: number;
  distanceMeters: number;
  expiresAt: string;
}

export interface ProviderProfile {
  id: string;
  businessName: string;
  approvalStatus: "Pending" | "Approved" | "Rejected" | "Suspended";
  isAvailable: boolean;
  serviceRadiusMeters: number;
  categories: ServiceCategory[];
  averageRating: number;
  reviewCount: number;
  latitude: number | null;
  longitude: number | null;
}

export interface Job {
  caseId: string;
  status: CaseStatus;
  incidentType: IncidentType;
  category: ServiceCategory;
  description: string | null;
  createdAt: string;
}

export interface PendingProvider {
  id: string;
  businessName: string;
  licenseNumber: string | null;
  createdAt: string;
  phone: string | null;
  categories: ServiceCategory[];
}

// ---------------------------------------------------------------- admin: customers, providers, requests

export interface Page<T> {
  items: T[];
  total: number;
}

export interface AdminCustomer {
  id: string;
  name: string;
  email: string | null;
  loginPhone: string | null;
  joinedAt: string;
  totalCases: number;
  openCases: number;
  lastCaseAt: string | null;
}

export interface AdminProvider {
  id: string;
  businessName: string;
  ownerName: string;
  phone: string | null;
  email: string | null;
  loginPhone: string | null;
  approvalStatus: ProviderProfile["approvalStatus"];
  isAvailable: boolean;
  categories: ServiceCategory[];
  averageRating: number;
  reviewCount: number;
  activeJobs: number;
  completedJobs: number;
  latitude: number | null;
  longitude: number | null;
}

/** The provider who holds one service of a request. */
export interface AdminAssignment {
  category: ServiceCategory;
  providerId: string;
  businessName: string;
  phone: string | null;
}

export interface AdminCaseRow {
  id: string;
  status: CaseStatus;
  incidentType: IncidentType;
  description: string | null;
  createdAt: string;
  updatedAt: string;
  customerId: string;
  customerName: string;
  customerContact: string | null;
  categories: ServiceCategory[];
  assigned: AdminAssignment[];
  openOffers: number;
}

export interface AdminSlot {
  category: ServiceCategory;
  assigned: AdminAssignment | null;
  openOffers: number;
}

export interface AdminCaseDetail {
  id: string;
  status: CaseStatus;
  incidentType: IncidentType;
  description: string | null;
  address: string | null;
  latitude: number;
  longitude: number;
  createdAt: string;
  resolvedAt: string | null;
  customerId: string;
  customerName: string;
  customerEmail: string | null;
  customerPhone: string | null;
  slots: AdminSlot[];
  canAssign: boolean;
  canUnassign: boolean;
  vehicle: Vehicle | null;
}

export interface AdminCandidate {
  providerId: string;
  businessName: string;
  phone: string | null;
  distanceMeters: number;
  isAvailable: boolean;
  /** The request is inside the area this provider said they cover. */
  withinOwnRadius: boolean;
  averageRating: number;
  reviewCount: number;
  activeJobs: number;
  categories: ServiceCategory[];
  assigned: boolean;
}
