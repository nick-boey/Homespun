/**
 * Loads the inventory-log-record JSON Schema co-located with the worker's
 * session-inventory service and exports an Ajv-backed validator. The validator
 * throws on any invalid record so schema drift surfaces directly in test
 * assertions (INV-5 / FR-008).
 */
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";
import Ajv2020 from "ajv/dist/2020.js";
import addFormatsImport from "ajv-formats";

const here = dirname(fileURLToPath(import.meta.url));
const schemaPath = resolve(
  here,
  "../../../src/Homespun.Worker/src/services/session-inventory.schema.json",
);

const schema = JSON.parse(readFileSync(schemaPath, "utf8"));

// ajv-formats ships as both ESM default and CJS — normalize.
const addFormats = (addFormatsImport as unknown as { default?: typeof addFormatsImport }).default
  ?? addFormatsImport;
// Same for ajv's 2020 entrypoint.
const AjvCtor = (Ajv2020 as unknown as { default?: typeof Ajv2020 }).default ?? Ajv2020;

const ajv = new AjvCtor({ allErrors: true, strict: false });
addFormats(ajv);

const validateFn = ajv.compile(schema);

export function assertValidInventoryRecord(record: unknown): void {
  const ok = validateFn(record);
  if (!ok) {
    const errors = validateFn.errors ?? [];
    const formatted = errors
      .map((e) => `${e.instancePath || "/"} ${e.message} ${JSON.stringify(e.params ?? {})}`)
      .join("\n  ");
    throw new Error(
      `inventory-log-record.schema.json validation failed:\n  ${formatted}`,
    );
  }
}

export const inventorySchemaValidator = validateFn;
