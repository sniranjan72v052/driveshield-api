import type { ProviderDetails } from "../api/types";

/** The details form keeps everything as text while typing; toDetails turns it into what the API expects. */
export interface DetailsForm {
  licenseNumber: string;
  address: string;
  city: string;
  state: string;
  pinCode: string;
  managerName: string;
  managerEmail: string;
  panNumber: string;
  bankAccountNumber: string;
  ifscCode: string;
  tdsPercentage: string;
  zone: string;
  paintDiscount: string;
  paintDiscountOem: string;
  imdCode: string;
}

export const emptyDetails: DetailsForm = {
  licenseNumber: "",
  address: "",
  city: "",
  state: "",
  pinCode: "",
  managerName: "",
  managerEmail: "",
  panNumber: "",
  bankAccountNumber: "",
  ifscCode: "",
  tdsPercentage: "",
  zone: "",
  paintDiscount: "",
  paintDiscountOem: "",
  imdCode: "",
};

export function fromDetails(d: ProviderDetails): DetailsForm {
  const s = (v: string | number | null) => (v === null || v === undefined ? "" : String(v));
  return {
    licenseNumber: s(d.licenseNumber),
    address: s(d.address),
    city: s(d.city),
    state: s(d.state),
    pinCode: s(d.pinCode),
    managerName: s(d.managerName),
    managerEmail: s(d.managerEmail),
    panNumber: s(d.panNumber),
    bankAccountNumber: s(d.bankAccountNumber),
    ifscCode: s(d.ifscCode),
    tdsPercentage: s(d.tdsPercentage),
    zone: s(d.zone),
    paintDiscount: s(d.paintDiscount),
    paintDiscountOem: s(d.paintDiscountOem),
    imdCode: s(d.imdCode),
  };
}

export function toDetails(f: DetailsForm): ProviderDetails {
  const text = (v: string) => v.trim() || null;
  const num = (v: string) => (v.trim() === "" || Number.isNaN(Number(v)) ? null : Number(v));
  return {
    licenseNumber: text(f.licenseNumber),
    address: text(f.address),
    city: text(f.city),
    state: text(f.state),
    pinCode: text(f.pinCode),
    managerName: text(f.managerName),
    managerEmail: text(f.managerEmail),
    panNumber: text(f.panNumber),
    bankAccountNumber: text(f.bankAccountNumber),
    ifscCode: text(f.ifscCode),
    tdsPercentage: num(f.tdsPercentage),
    zone: text(f.zone),
    paintDiscount: num(f.paintDiscount),
    paintDiscountOem: num(f.paintDiscountOem),
    imdCode: text(f.imdCode),
  };
}

interface Props {
  value: DetailsForm;
  onChange: (next: DetailsForm) => void;
}

/** Business details a provider can give: address, manager, tax and bank details, commercial terms. All optional. */
export default function ProviderDetailsFields({ value, onChange }: Props) {
  const set = (key: keyof DetailsForm) => (e: { target: { value: string } }) => onChange({ ...value, [key]: e.target.value });

  return (
    <>
      <div className="stack">
        <label htmlFor="lic">Licence or GST No (optional)</label>
        <input id="lic" value={value.licenseNumber} onChange={set("licenseNumber")} maxLength={100} />
      </div>

      <fieldset className="stack">
        <legend>Business address</legend>
        <label htmlFor="addr">Address</label>
        <textarea id="addr" rows={2} value={value.address} onChange={set("address")} maxLength={300} />
        <label htmlFor="city">City</label>
        <input id="city" value={value.city} onChange={set("city")} maxLength={100} autoComplete="address-level2" />
        <label htmlFor="state">State</label>
        <input id="state" value={value.state} onChange={set("state")} maxLength={100} autoComplete="address-level1" />
        <label htmlFor="pin">Pin code</label>
        <input id="pin" value={value.pinCode} onChange={set("pinCode")} inputMode="numeric" maxLength={6} autoComplete="postal-code" />
        <label htmlFor="zone">Zone</label>
        <input id="zone" value={value.zone} onChange={set("zone")} maxLength={50} />
      </fieldset>

      <fieldset className="stack">
        <legend>Manager</legend>
        <label htmlFor="mgr">Manager name</label>
        <input id="mgr" value={value.managerName} onChange={set("managerName")} maxLength={100} />
        <label htmlFor="mgrmail">Manager email</label>
        <input id="mgrmail" type="email" value={value.managerEmail} onChange={set("managerEmail")} maxLength={254} autoCapitalize="none" />
      </fieldset>

      <fieldset className="stack">
        <legend>Tax and bank details</legend>
        <label htmlFor="pan">PAN number</label>
        <input id="pan" value={value.panNumber} onChange={set("panNumber")} maxLength={10} autoCapitalize="characters" placeholder="ABCDE1234F" />
        <label htmlFor="acct">Bank account number</label>
        <input id="acct" value={value.bankAccountNumber} onChange={set("bankAccountNumber")} inputMode="numeric" maxLength={18} autoComplete="off" />
        <label htmlFor="ifsc">IFSC code</label>
        <input id="ifsc" value={value.ifscCode} onChange={set("ifscCode")} maxLength={11} autoCapitalize="characters" placeholder="HDFC0001234" />
        <label htmlFor="tds">TDS percentage</label>
        <input id="tds" type="number" min={0} max={100} step="0.01" value={value.tdsPercentage} onChange={set("tdsPercentage")} />
      </fieldset>

      <fieldset className="stack">
        <legend>Commercial terms</legend>
        <label htmlFor="paint">Paint discount (%)</label>
        <input id="paint" type="number" min={0} max={100} step="0.01" value={value.paintDiscount} onChange={set("paintDiscount")} />
        <label htmlFor="paintoem">Paint discount OEM (%)</label>
        <input id="paintoem" type="number" min={0} max={100} step="0.01" value={value.paintDiscountOem} onChange={set("paintDiscountOem")} />
        <label htmlFor="imd">IMD code</label>
        <input id="imd" value={value.imdCode} onChange={set("imdCode")} maxLength={30} />
      </fieldset>
    </>
  );
}
