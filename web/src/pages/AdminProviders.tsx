import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api, messageOf } from "../api/client";
import type { AdminProvider, Page } from "../api/types";
import { CATEGORY_LABEL } from "../api/labels";
import AdminGate from "../components/AdminGate";

const PAGE = 40;
const APPROVALS = ["", "Approved", "Pending", "Rejected", "Suspended"] as const;

/** All providers, whatever their approval status, with their current and finished jobs. */
export default function AdminProviders() {
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [approval, setApproval] = useState<(typeof APPROVALS)[number]>("");
  const [rows, setRows] = useState<AdminProvider[] | null>(null);
  const [total, setTotal] = useState(0);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const t = setTimeout(() => setTerm(search.trim()), 350);
    return () => clearTimeout(t);
  }, [search]);

  const query = useCallback(
    (skip: number) => {
      const p = new URLSearchParams({ take: String(PAGE), skip: String(skip) });
      if (term) p.set("search", term);
      if (approval) p.set("approval", approval);
      return api.get<Page<AdminProvider>>(`/api/admin/providers?${p}`);
    },
    [term, approval],
  );

  useEffect(() => {
    let current = true;
    setRows(null);
    query(0)
      .then((page) => {
        if (!current) return;
        setRows(page.items);
        setTotal(page.total);
        setError(null);
      })
      .catch((e) => current && setError(messageOf(e)));
    return () => {
      current = false;
    };
  }, [query]);

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
    <AdminGate title="Providers">
      <div className="stack">
        <label htmlFor="prov-search">Search by business, owner, email or phone</label>
        <input id="prov-search" type="search" value={search} onChange={(e) => setSearch(e.target.value)} />
        <label htmlFor="prov-approval">Approval</label>
        <select id="prov-approval" value={approval} onChange={(e) => setApproval(e.target.value as (typeof APPROVALS)[number])}>
          {APPROVALS.map((a) => (
            <option key={a} value={a}>
              {a || "Any"}
            </option>
          ))}
        </select>
      </div>

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {rows === null && !error && <p className="muted">Loading…</p>}
      {rows && rows.length === 0 && <p>No providers match.</p>}

      {rows && rows.length > 0 && (
        <>
          <p className="muted" role="status">
            Showing {rows.length} of {total}
          </p>
          <ul className="rows admin-rows">
            {rows.map((p) => (
              <li key={p.id} className="admin-row">
                <div className="admin-row-main">
                  <div className="row wrap">
                    <strong>{p.businessName}</strong>
                    <span className={`tag tag-${p.approvalStatus}`}>{p.approvalStatus}</span>
                    {p.approvalStatus === "Approved" && (
                      <span className={`tag ${p.isAvailable ? "tag-on" : "tag-off"}`}>{p.isAvailable ? "Available" : "Offline"}</span>
                    )}
                  </div>
                  <div className="muted">{p.categories.map((c) => CATEGORY_LABEL[c]).join(", ")}</div>
                  <div className="muted">
                    {[p.ownerName, p.phone ?? p.loginPhone, p.email].filter(Boolean).join(" · ")}
                  </div>
                  <div>
                    {p.activeJobs} active, {p.completedJobs} finished
                    {p.reviewCount > 0 && (
                      <span className="muted">
                        {" "}
                        · {p.averageRating.toFixed(1)} stars from {p.reviewCount}
                      </span>
                    )}
                  </div>
                </div>
                <div className="admin-row-actions">
                  <Link className="btn" to={`/admin/requests?providerId=${p.id}&status=all`}>
                    View jobs
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
