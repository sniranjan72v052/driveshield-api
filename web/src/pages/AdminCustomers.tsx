import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api, messageOf } from "../api/client";
import type { AdminCustomer, Page } from "../api/types";
import { formatTime } from "../api/labels";
import AdminGate from "../components/AdminGate";

const PAGE = 40;

/** Everyone who has asked for help, with how many requests they have and how many are still open. */
export default function AdminCustomers() {
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [rows, setRows] = useState<AdminCustomer[] | null>(null);
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
      return api.get<Page<AdminCustomer>>(`/api/admin/customers?${p}`);
    },
    [term],
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
    <AdminGate title="Customers">
      <div className="stack">
        <label htmlFor="cust-search">Search by name, email or mobile</label>
        <input id="cust-search" type="search" value={search} onChange={(e) => setSearch(e.target.value)} />
      </div>

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {rows === null && !error && <p className="muted">Loading…</p>}
      {rows && rows.length === 0 && <p>No customers match.</p>}

      {rows && rows.length > 0 && (
        <>
          <p className="muted" role="status">
            Showing {rows.length} of {total}
          </p>
          <ul className="rows admin-rows">
            {rows.map((c) => (
              <li key={c.id} className="admin-row">
                <div className="admin-row-main">
                  <strong>{c.name || c.email || c.loginPhone || "Customer"}</strong>
                  <div className="muted">{[c.email, c.loginPhone].filter(Boolean).join(" · ") || "No contact saved"}</div>
                  <div>
                    {c.totalCases} {c.totalCases === 1 ? "request" : "requests"}
                    {c.openCases > 0 && <strong>, {c.openCases} still open</strong>}
                  </div>
                  <div className="muted">
                    Joined {formatTime(c.joinedAt)}
                    {c.lastCaseAt ? `, last request ${formatTime(c.lastCaseAt)}` : ""}
                  </div>
                </div>
                <div className="admin-row-actions">
                  <Link className="btn" to={`/admin/requests?customerId=${c.id}&status=all`}>
                    View requests
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
