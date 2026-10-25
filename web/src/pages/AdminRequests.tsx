import { useCallback, useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api, messageOf } from "../api/client";
import type { AdminCaseRow, Page } from "../api/types";
import { CATEGORY_LABEL, INCIDENT_LABEL, STATUS_LABEL, formatTime } from "../api/labels";
import AdminGate from "../components/AdminGate";

const PAGE = 30;

const FILTERS: { key: string; label: string }[] = [
  { key: "all", label: "All" },
  { key: "open", label: "Open" },
  { key: "attention", label: "Needs our team" },
  { key: "active", label: "With a provider" },
  { key: "resolved", label: "Resolved" },
  { key: "Cancelled", label: "Cancelled" },
];

/** Every request with its current status and who is on it. Narrow it by status, customer, provider or a search. */
export default function AdminRequests() {
  const [params, setParams] = useSearchParams();
  const status = params.get("status") ?? "open";
  const customerId = params.get("customerId");
  const providerId = params.get("providerId");
  const q = params.get("q") ?? "";

  const [search, setSearch] = useState(q);
  const [rows, setRows] = useState<AdminCaseRow[] | null>(null);
  const [total, setTotal] = useState(0);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const setParam = useCallback(
    (key: string, value: string | null) => {
      const next = new URLSearchParams(params);
      if (value) next.set(key, value);
      else next.delete(key);
      setParams(next, { replace: true });
    },
    [params, setParams],
  );

  // Typing in the search box updates the list a moment after the last key.
  useEffect(() => {
    if (search === q) return;
    const t = setTimeout(() => setParam("q", search.trim() || null), 350);
    return () => clearTimeout(t);
  }, [search, q, setParam]);

  const query = useCallback(
    (skip: number) => {
      const p = new URLSearchParams({ status, take: String(PAGE), skip: String(skip) });
      if (customerId) p.set("customerId", customerId);
      if (providerId) p.set("providerId", providerId);
      if (q) p.set("search", q);
      return api.get<Page<AdminCaseRow>>(`/api/admin/cases?${p}`);
    },
    [status, customerId, providerId, q],
  );

  const reload = useCallback(async () => {
    try {
      const page = await query(0);
      setRows(page.items);
      setTotal(page.total);
      setError(null);
    } catch (e) {
      setError(messageOf(e));
    }
  }, [query]);

  useEffect(() => {
    setRows(null);
    void reload();
    // New requests and status changes show up without a click.
    const t = setInterval(() => void reload(), 20000);
    return () => clearInterval(t);
  }, [reload]);

  const more = async () => {
    if (!rows) return;
    setLoadingMore(true);
    try {
      const page = await query(rows.length);
      setRows([...rows, ...page.items]);
      setTotal(page.total);
    } catch (e) {
      setError(messageOf(e));
    } finally {
      setLoadingMore(false);
    }
  };

  return (
    <AdminGate title="Requests">
      <div className="filters" role="group" aria-label="Show requests">
        {FILTERS.map((f) => (
          <button
            key={f.key}
            type="button"
            className={`filter${status === f.key ? " on" : ""}`}
            aria-pressed={status === f.key}
            onClick={() => setParam("status", f.key === "open" ? null : f.key)}
          >
            {f.label}
          </button>
        ))}
      </div>

      <div className="stack">
        <label htmlFor="req-search">Search by customer name, email, mobile or what they wrote</label>
        <input id="req-search" type="search" value={search} onChange={(e) => setSearch(e.target.value)} />
      </div>

      {(customerId || providerId) && (
        <p className="banner">
          Showing {customerId ? "one customer's" : "one provider's"} requests.{" "}
          <button
            type="button"
            className="linklike"
            onClick={() => {
              const next = new URLSearchParams(params);
              next.delete("customerId");
              next.delete("providerId");
              setParams(next, { replace: true });
            }}
          >
            Show everyone
          </button>
        </p>
      )}

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {rows === null && !error && <p className="muted">Loading…</p>}
      {rows && rows.length === 0 && <p>No requests match.</p>}

      {rows && rows.length > 0 && (
        <>
          <p className="muted" role="status">
            Showing {rows.length} of {total}
          </p>
          <ul className="rows admin-rows">
            {rows.map((r) => (
              <li key={r.id} className="admin-row">
                <div className="admin-row-main">
                  <div className="row wrap">
                    <strong>{INCIDENT_LABEL[r.incidentType]}</strong>
                    <span className={`status status-${r.status}`}>{STATUS_LABEL[r.status]}</span>
                  </div>
                  <div className="muted">{r.categories.map((c) => CATEGORY_LABEL[c]).join(", ")}</div>
                  {r.description && <p className="said">&ldquo;{r.description}&rdquo;</p>}
                  <div>
                    Customer:{" "}
                    <Link to={`/admin/requests?customerId=${r.customerId}&status=all`}>{r.customerName}</Link>
                    {r.customerContact && <span className="muted"> ({r.customerContact})</span>}
                  </div>
                  <div>
                    {r.assigned.length > 0 ? (
                      r.assigned.map((a) => (
                        <div key={`${a.category}-${a.providerId}`}>
                          <Link to={`/admin/requests?providerId=${a.providerId}&status=all`}>{a.businessName}</Link>
                          <span className="muted"> for {CATEGORY_LABEL[a.category].toLowerCase()}</span>
                        </div>
                      ))
                    ) : (
                      <span className="muted">
                        No provider yet{r.openOffers > 0 ? `, asked ${r.openOffers} so far` : ""}
                      </span>
                    )}
                  </div>
                  <div className="muted">
                    Reported {formatTime(r.createdAt)}, updated {formatTime(r.updatedAt)}
                  </div>
                </div>
                <div className="admin-row-actions">
                  <Link className="btn primary" to={`/admin/requests/${r.id}`}>
                    Manage
                  </Link>
                </div>
              </li>
            ))}
          </ul>
          {rows.length < total && (
            <p>
              <button className="btn" onClick={() => void more()} disabled={loadingMore}>
                {loadingMore ? "Loading…" : "Show more"}
              </button>
            </p>
          )}
        </>
      )}
    </AdminGate>
  );
}
