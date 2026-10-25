import { useEffect, useRef } from "react";
import * as L from "leaflet";
import "leaflet/dist/leaflet.css";

export interface LatLng {
  lat: number;
  lng: number;
}

interface Props {
  value: LatLng | null;
  /** When set, clicking the map moves the pin and reports the new position. */
  onChange?: (p: LatLng) => void;
  /** Show a ~150 m circle instead of an exact pin. */
  approximate?: boolean;
  height?: number;
}

// Centre of India, shown until a location is known.
const DEFAULT_CENTER: L.LatLngTuple = [20.5937, 78.9629];

const pinIcon = L.divIcon({
  className: "map-pin",
  html: "<span></span>",
  iconSize: [28, 28],
  iconAnchor: [14, 28],
});

export default function MapView({ value, onChange, approximate = false, height = 320 }: Props) {
  const el = useRef<HTMLDivElement>(null);
  const map = useRef<L.Map | null>(null);
  const layer = useRef<L.Layer | null>(null);
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;

  // Create the map once.
  useEffect(() => {
    if (!el.current || map.current) return;
    const m = L.map(el.current).setView(value ? [value.lat, value.lng] : DEFAULT_CENTER, value ? 15 : 5);
    L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
      maxZoom: 19,
      attribution: "&copy; OpenStreetMap contributors",
    }).addTo(m);
    m.on("click", (e: L.LeafletMouseEvent) => {
      onChangeRef.current?.({ lat: e.latlng.lat, lng: e.latlng.lng });
    });
    map.current = m;
    return () => {
      m.remove();
      map.current = null;
      layer.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Keep the pin in step with the value.
  useEffect(() => {
    const m = map.current;
    if (!m) return;
    if (layer.current) {
      layer.current.remove();
      layer.current = null;
    }
    if (!value) return;
    const pos: L.LatLngTuple = [value.lat, value.lng];
    layer.current = approximate
      ? L.circle(pos, { radius: 150, color: "#0e5a73", fillColor: "#0e5a73", fillOpacity: 0.25 }).addTo(m)
      : L.marker(pos, { icon: pinIcon }).addTo(m);
    m.setView(pos, Math.max(m.getZoom(), 14));
  }, [value?.lat, value?.lng, approximate]);

  return (
    <div
      ref={el}
      className="map"
      style={{ height }}
      role="img"
      aria-label={value ? "Map showing the selected location" : "Map. Choose a location."}
    />
  );
}

/** Asks the browser for the device position (needs https or localhost). */
export function getMyLocation(): Promise<LatLng> {
  return new Promise((resolve, reject) => {
    if (!navigator.geolocation) {
      reject(new Error("This browser can't share your location. Tap the map to place the pin."));
      return;
    }
    navigator.geolocation.getCurrentPosition(
      (pos) => resolve({ lat: pos.coords.latitude, lng: pos.coords.longitude }),
      () => reject(new Error("We couldn't get your location. Allow location access, or tap the map to place the pin.")),
      { enableHighAccuracy: true, timeout: 15000 },
    );
  });
}
