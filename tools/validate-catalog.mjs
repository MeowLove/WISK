import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const catalogPath = path.join(root, "catalog", "index.json");
const schemaPath = path.join(root, "schema", "catalog.schema.json");

const readJson = async (filePath) => JSON.parse(await readFile(filePath, "utf8"));
const fail = (message) => {
  console.error(`WISK catalog invalid: ${message}`);
  process.exitCode = 1;
};

const catalog = await readJson(catalogPath);
await readJson(schemaPath);

const isId = (value) => typeof value === "string" && /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(value);
const isSemver = (value) => typeof value === "string" && /^\d+\.\d+\.\d+$/.test(value);
const isUrl = (value) => {
  try {
    return ["http:", "https:"].includes(new URL(value).protocol);
  } catch {
    return false;
  }
};
const idsAreUnique = (items, label) => {
  const ids = items.map((item) => item.id);
  const duplicates = ids.filter((id, index) => ids.indexOf(id) !== index);
  if (duplicates.length > 0) {
    fail(`${label} contains duplicate id(s): ${[...new Set(duplicates)].join(", ")}`);
  }
};

if (catalog.schemaVersion !== 1) fail("schemaVersion must be 1");
if (!isSemver(catalog.catalogVersion)) fail("catalogVersion must use x.y.z format");
if (!/^\d{4}-\d{2}-\d{2}$/.test(catalog.updatedAt)) fail("updatedAt must use YYYY-MM-DD format");
if (!catalog.publisher || !isId(catalog.publisher.id) || !catalog.publisher.name || !isUrl(catalog.publisher.website)) {
  fail("publisher must contain a valid id, name, and HTTP(S) website");
}

const repositories = Array.isArray(catalog.repositories) ? catalog.repositories : [];
const applications = Array.isArray(catalog.applications) ? catalog.applications : [];
idsAreUnique(repositories, "repositories");
idsAreUnique(applications, "applications");

const repositoryIds = new Set(repositories.map((repository) => repository.id));
const applicationIds = new Set(applications.map((application) => application.id));
for (const repository of repositories) {
  if (!isId(repository.id)) fail(`invalid repository id: ${repository.id}`);
  if (repository.visibility !== "public") fail(`repository ${repository.id} must be public`);
  if (!isUrl(repository.url)) fail(`repository ${repository.id} has an invalid URL`);
}

for (const application of applications) {
  if (!isId(application.id)) fail(`invalid application id: ${application.id}`);
  if (!isSemver(application.version)) fail(`application ${application.id} has an invalid version`);
  if (!repositoryIds.has(application.repositoryId)) {
    fail(`application ${application.id} references unknown repository ${application.repositoryId}`);
  }
  if (!isUrl(application.releasePage)) fail(`application ${application.id} has an invalid release page`);
  if (!Array.isArray(application.platforms) || application.platforms.length === 0) {
    fail(`application ${application.id} must declare at least one platform`);
  }
  if (!application.storeEntries || typeof application.storeEntries.software !== "boolean" || typeof application.storeEntries.settings !== "boolean") {
    fail(`application ${application.id} must declare software and settings store placement`);
  }
}

const stores = catalog.stores ?? {};
for (const storeName of ["software", "settings"]) {
  const entries = Array.isArray(stores[storeName]) ? stores[storeName] : [];
  const seen = new Set();
  for (const entry of entries) {
    if (!applicationIds.has(entry.applicationId)) {
      fail(`${storeName} store references unknown application ${entry.applicationId}`);
    }
    if (seen.has(entry.applicationId)) fail(`${storeName} store contains duplicate application ${entry.applicationId}`);
    seen.add(entry.applicationId);
    if (typeof entry.enabled !== "boolean" || !Number.isInteger(entry.order) || entry.order < 0) {
      fail(`${storeName} store entry ${entry.applicationId} has invalid enabled/order values`);
    }
  }
}

if (process.exitCode !== 1) {
  console.log(`WISK catalog valid: ${repositories.length} repositories, ${applications.length} application(s), ${stores.software.length} software-store entry(ies), ${stores.settings.length} settings-store entry(ies).`);
}

