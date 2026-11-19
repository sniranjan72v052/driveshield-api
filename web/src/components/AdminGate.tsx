import type { ReactNode } from "react";
import { useAuth } from "../auth";
import AdminNav from "./AdminNav";
<<<<<<< HEAD
import { isStaff } from "../api/types";
=======
>>>>>>> ea6e7ce160a55737ca5f595cc1b53b1aa83f75a3

/** Shows an admin page's title and menu to admins, and a short message to everyone else. */
export default function AdminGate({ title, children }: { title: string; children: ReactNode }) {
  const { me } = useAuth();
<<<<<<< HEAD
  if (!isStaff(me?.role)) {
=======
  if (me?.role !== "Admin") {
>>>>>>> ea6e7ce160a55737ca5f595cc1b53b1aa83f75a3
    return (
      <main>
        <h1>Admin</h1>
        <p>This area is for administrators.</p>
      </main>
    );
  }
  return (
    <main>
      <h1>{title}</h1>
      <AdminNav />
      {children}
    </main>
  );
}
