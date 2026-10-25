import { useCallback, useEffect, useState } from "react";
import { api, messageOf } from "../api/client";
import type { Vehicle } from "../api/types";
import { useAuth } from "../auth";
import VehicleForm from "../components/VehicleForm";
import VehicleSummary from "../components/VehicleSummary";

/** The customer's saved vehicles: add, edit or remove. One is chosen when reporting a problem. */
export default function Vehicles() {
  const { reloadMe } = useAuth();
  const [list, setList] = useState<Vehicle[] | null>(null);
  const [editing, setEditing] = useState<string | "new" | null>(null);
  const [removing, setRemoving] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setList(await api.get<Vehicle[]>("/api/vehicles"));
      setError(null);
    } catch (e) {
      setError(messageOf(e));
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const saved = async () => {
    setEditing(null);
    await load();
    await reloadMe();
  };

  const remove = async (v: Vehicle) => {
    try {
      await api.delete(`/api/vehicles/${v.id}`);
      setRemoving(null);
      await load();
      await reloadMe();
    } catch (e) {
      setError(messageOf(e));
    }
  };

  return (
    <main>
      <h1>Your vehicles</h1>
      <p className="lead">Save your vehicle once. When you ask for help, the provider sees what they are coming to.</p>

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {list === null && !error && <p className="muted">Loading…</p>}
      {list && list.length === 0 && editing !== "new" && <p>You have not added a vehicle yet.</p>}

      {list && list.length > 0 && (
        <ul className="rows">
          {list.map((v) => (
            <li key={v.id} className="vehicle-row">
              {editing === v.id ? (
                <VehicleForm vehicle={v} onSaved={saved} onCancel={() => setEditing(null)} />
              ) : (
                <>
                  <VehicleSummary vehicle={v} />
                  {removing === v.id ? (
                    <div className="comment-confirm">
                      <p>Remove {v.make} {v.model}? Requests you made earlier are kept.</p>
                      <div className="row wrap">
                        <button type="button" className="btn emergency-lite" onClick={() => void remove(v)}>
                          Remove
                        </button>
                        <button type="button" className="btn quiet" onClick={() => setRemoving(null)}>
                          Keep it
                        </button>
                      </div>
                    </div>
                  ) : (
                    <div className="row wrap">
                      <button type="button" className="linklike" onClick={() => setEditing(v.id)}>
                        Edit
                      </button>
                      <button type="button" className="linklike" onClick={() => setRemoving(v.id)}>
                        Remove
                      </button>
                    </div>
                  )}
                </>
              )}
            </li>
          ))}
        </ul>
      )}

      {editing === "new" ? (
        <section className="stack">
          <h2>Add a vehicle</h2>
          <VehicleForm onSaved={saved} onCancel={() => setEditing(null)} submitLabel="Add vehicle" />
        </section>
      ) : (
        <p>
          <button type="button" className="btn primary" onClick={() => setEditing("new")}>
            Add a vehicle
          </button>
        </p>
      )}
    </main>
  );
}
