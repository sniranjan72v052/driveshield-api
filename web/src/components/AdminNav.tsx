import { NavLink } from "react-router-dom";

/** The parts of the admin area. */
export default function AdminNav() {
  return (
    <nav className="subnav" aria-label="Admin">
      <NavLink to="/admin/requests">Requests</NavLink>
      <NavLink to="/admin/customers">Customers</NavLink>
      <NavLink to="/admin/providers">Providers</NavLink>
      <NavLink to="/admin" end>
        Approvals
      </NavLink>
      <NavLink to="/admin/photos">Photos</NavLink>
    </nav>
  );
}
