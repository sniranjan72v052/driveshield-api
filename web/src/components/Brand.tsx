import { Link } from "react-router-dom";
import { ShieldMark } from "./Icons";

/** The logo and name. Always links home. */
export default function Brand() {
  return (
    <Link to="/" className="brand" aria-label="DriveShield home">
      <span className="brand-mark">
        <ShieldMark />
      </span>
      <span className="brand-text">
        <strong>DriveShield</strong>
        <small>Achha Kiya, Safe Raha</small>
      </span>
    </Link>
  );
}
