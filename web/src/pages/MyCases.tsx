import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api, messageOf } from "../api/client";
import type { CaseSummary } from "../api/types";
import { INCIDENT_LABEL, STATUS_LABEL, formatTime } from "../api/labels";

export default function MyCases() {
  const [cases, setCases] = useState<CaseSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .get<CaseSummary[]>("/api/cases/mine")
      .then(setCases)
      .catch((e) => setError(messageOf(e)));
  }, []);

  return (
    <main>
      <h1>My cases</h1>
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {cases && cases.length === 0 && (
        <p>
          You haven't reported anything yet. When you need help, <Link to="/help">report an incident</Link>.
        </p>
      )}
      {cases && cases.length > 0 && (
        <ul className="rows">
          {cases.map((c) => (
            <li key={c.id}>
              <Link to={`/case/${c.id}`} className="row-link">
                <span className="row-title">{INCIDENT_LABEL[c.incidentType]}</span>
                <span className={`status status-${c.status}`}>{STATUS_LABEL[c.status]}</span>
                <span className="muted">{formatTime(c.createdAt)}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </main>
  );
}
