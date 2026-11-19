import { NavLink } from "react-router-dom";
import { useAuth } from "../auth";

/** The parts of the admin area. */
export default function AdminNav() {
  const { me } = useAuth();
  return (
    <nav className="subnav" aria-label="Admin">
      <NavLink to="/admin/requests">Requests</NavLink>
      <NavLink to="/admin/customers">Customers</NavLink>
      <NavLink to="/admin/providers">Providers</NavLink>
      <NavLink to="/admin" end>
        Approvals
      </NavLink>
      <NavLink to="/admin/photos">Photos</NavLink>
      {me?.role === "SuperAdmin" && <NavLink to="/admin/admins">Admins</NavLink>}
    </nav>
  );
}
