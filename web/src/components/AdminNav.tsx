import { NavLink } from "react-router-dom";
<<<<<<< HEAD
import { useAuth } from "../auth";

/** The parts of the admin area. */
export default function AdminNav() {
  const { me } = useAuth();
=======

/** The parts of the admin area. */
export default function AdminNav() {
>>>>>>> ea6e7ce160a55737ca5f595cc1b53b1aa83f75a3
  return (
    <nav className="subnav" aria-label="Admin">
      <NavLink to="/admin/requests">Requests</NavLink>
      <NavLink to="/admin/customers">Customers</NavLink>
      <NavLink to="/admin/providers">Providers</NavLink>
      <NavLink to="/admin" end>
        Approvals
      </NavLink>
      <NavLink to="/admin/photos">Photos</NavLink>
<<<<<<< HEAD
      {me?.role === "SuperAdmin" && <NavLink to="/admin/admins">Admins</NavLink>}
=======
>>>>>>> ea6e7ce160a55737ca5f595cc1b53b1aa83f75a3
    </nav>
  );
}
