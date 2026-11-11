import { useAuth } from "../auth";
import VehicleForm from "../components/VehicleForm";

/** Shown once to a new customer, after their name. Skippable: they can add a vehicle later under Your account. */
export default function VehiclePrompt({ onSkip }: { onSkip: () => void }) {
  const { reloadMe } = useAuth();
  return (
    <main className="signin">
      <h1>Your vehicle</h1>
      <p className="lead">Tell us about your vehicle once. When you ask for help, the provider already knows what they are coming to.</p>
      <VehicleForm submitLabel="Save and continue" onSaved={() => reloadMe()} onCancel={onSkip} cancelLabel="Skip for now" />
    </main>
  );
}
