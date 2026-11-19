-- Adds the business details providers can give: address, manager, PAN, bank, TDS, zone, paint discounts, IMD code.
-- Safe to run more than once. Keep this file free of curly braces: the API runs it through a format-sensitive call.

ALTER TABLE provider_profiles ADD COLUMN IF NOT EXISTS address             text;
ALTER TABLE provider_profiles ADD COLUMN IF NOT EXISTS city                text;
ALTER TABLE provider_profiles ADD COLUMN IF NOT EXISTS state               text;
ALTER TABLE provider_profiles ADD COLUMN IF NOT EXISTS pin_code            varchar(6);
ALTER TABLE provider_profiles ADD COLUMN IF NOT EXISTS manager_name        text;
ALTER TABLE provider_profiles ADD COLUMN IF NOT EXISTS manager_email       text;
ALTER TABLE provider_profiles ADD COLUMN IF NOT EXISTS pan_number          varchar(10);
ALTER TABLE provider_profiles ADD COLUMN IF NOT EXISTS bank_account_number varchar(18);
ALTER TABLE provider_profiles ADD COLUMN IF NOT EXISTS ifsc_code           varchar(11);
ALTER TABLE provider_profiles ADD COLUMN IF NOT EXISTS tds_percentage      numeric(5,2);
ALTER TABLE provider_profiles ADD COLUMN IF NOT EXISTS zone                text;
ALTER TABLE provider_profiles ADD COLUMN IF NOT EXISTS paint_discount      numeric(5,2);
ALTER TABLE provider_profiles ADD COLUMN IF NOT EXISTS paint_discount_oem  numeric(5,2);
ALTER TABLE provider_profiles ADD COLUMN IF NOT EXISTS imd_code            text;
