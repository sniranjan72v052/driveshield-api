import type { Vehicle } from "../api/types";

/** One vehicle in a few lines, for case pages and the admin view. */
export default function VehicleSummary({ vehicle }: { vehicle: Vehicle }) {
  return (
    <div className="vehicle-card">
      <strong>
        {vehicle.make} {vehicle.model}
        {vehicle.isElectric ? " (electric)" : ""}
      </strong>
      <span className="reg-plate">{vehicle.registrationNumber}</span>
      {(vehicle.insurerName || vehicle.policyNumber) && (
        <span className="muted">
          Insurance: {[vehicle.insurerName, vehicle.policyNumber].filter(Boolean).join(", policy ")}
        </span>
      )}
    </div>
  );
}
