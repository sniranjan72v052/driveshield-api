import { Link } from "react-router-dom";

export default function SiteFooter() {
  return (
    <footer className="footer">
      <div className="footer-inner">
        <span>
          &copy; {new Date().getFullYear()} DriveShield. Achha Kiya, Safe Raha.
        </span>
        <nav className="footer-links" aria-label="Footer">
          <Link to="/">Home</Link>
          <Link to="/provider">For providers</Link>
          <Link to="/help">Get help</Link>
        </nav>
      </div>
    </footer>
  );
}
