// Photos are shrunk in the browser before upload: phone cameras produce 3-8 MB files, but 1600 px is plenty to
// show damage or a repair, and it keeps uploads quick on mobile data. Re-drawing the picture also removes its
// hidden metadata, including the GPS position some cameras embed.

export const MAX_PHOTOS = 5;
const MAX_EDGE = 1600;
const MAX_BYTES = 5 * 1024 * 1024;
const ACCEPTED = ["image/jpeg", "image/png", "image/webp"];

export const ACCEPT_ATTR = ACCEPTED.join(",");

function baseName(name: string): string {
  const dot = name.lastIndexOf(".");
  const stem = dot > 0 ? name.slice(0, dot) : name;
  return stem.replace(/[^\w.-]+/g, "_").slice(0, 40) || "photo";
}

export async function prepareImage(file: File): Promise<File> {
  try {
    const bitmap = await createImageBitmap(file);
    const scale = Math.min(1, MAX_EDGE / Math.max(bitmap.width, bitmap.height));
    const w = Math.max(1, Math.round(bitmap.width * scale));
    const h = Math.max(1, Math.round(bitmap.height * scale));

    const canvas = document.createElement("canvas");
    canvas.width = w;
    canvas.height = h;
    const ctx = canvas.getContext("2d");
    if (!ctx) throw new Error("canvas unavailable");
    ctx.fillStyle = "#ffffff"; // PNG transparency would turn black in a JPEG
    ctx.fillRect(0, 0, w, h);
    ctx.drawImage(bitmap, 0, 0, w, h);
    bitmap.close();

    const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, "image/jpeg", 0.85));
    if (!blob) throw new Error("encode failed");
    return new File([blob], `${baseName(file.name)}.jpg`, { type: "image/jpeg" });
  } catch {
    // The browser could not process it. Send the original only if the server will accept it as is.
    if (ACCEPTED.includes(file.type) && file.size <= MAX_BYTES) return file;
    throw new Error(`"${file.name}" can't be used. Choose a JPEG, PNG or WebP photo under 5 MB.`);
  }
}
