import type { ReactNode } from "react";
import { useAuth } from "../auth";
import AdminNav from "./AdminNav";

/** Shows an admin page's title and menu to admins, and a short message to everyone else. */
export default function AdminGate({ title, children }: { title: string; children: ReactNode }) {
  const { me } = useAuth();
  if (me?.role !== "Admin") {
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
