// Run from any directory. The published profile is self-contained and reproducible.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const source = 'src/Packata.Documents/Validation/Schemas/datapackage.json';
const destination = 'docs/profiles/category-tables/1.0.0/datapackage.json';
const uri = 'https://raw.githubusercontent.com/Seddryck/Packata/profile-category-tables-v1.0.0/' + destination;
const schema = JSON.parse(fs.readFileSync(path.join(root, source), 'utf8'));
const conditions = JSON.parse(fs.readFileSync(new URL('./CategoryTablesProfile.conditions.json', import.meta.url), 'utf8'));
schema.$id = uri;
schema.title = 'Packata Category Tables profile 1.0.0';
schema.description = 'Data Package v2.0 with Category Tables recipe support. Packata compatibility profile; not the unmodified official v2.0 schema.';
schema.$comment = 'Derived from the pinned official Data Package v2.0 artifact (Unlicense). Recipe: frictionlessdata/datapackage@3b9efb7b0fa08f08b0d55d2ba86b2be271423f94. No external schema dependencies.';
schema.properties.$schema = { type: 'string', const: uri, default: uri };
schema.required = [...schema.required, '$schema'];
assert.equal(schema.definitions, undefined, 'Review base-schema changes before regenerating');
schema.definitions = {
  CategoryReference: {
    type: 'object', required: ['resource'], properties: {
      resource: { type: 'string', minLength: 1 },
      package: { type: 'string', minLength: 1 },
      encodedAs: { enum: ['value', 'label'] }
    }
  },
  CategoryFieldMap: {
    type: 'object', required: ['value'], properties: {
      value: { type: 'string', minLength: 1 },
      label: { type: 'string', minLength: 1 },
      ordered: { type: 'boolean' }
    }
  }
};

const resource = schema.properties.resources.items;
resource.properties.categoryFieldMap = { $ref: '#/definitions/CategoryFieldMap' };
resource.allOf = [...(resource.allOf ?? []), conditions.categoryTable];
const fields = resource.properties.schema.properties.fields.items;
const widened = [];
for (const branch of fields.oneOf) {
  const types = branch.properties.type.enum;
  if (!types.some(type => ['string', 'integer'].includes(type))) continue;
  assert.ok(branch.properties.categories.anyOf.every(option => option.type === 'array'));
  branch.properties.categories.anyOf.push({ $ref: '#/definitions/CategoryReference' });
  widened.push(...types);
}
assert.deepEqual(widened, ['string', 'integer']);
fields.allOf = [...(fields.allOf ?? []), conditions.categoryReference];

const output = JSON.stringify(schema, null, 2) + '\n';
if (process.argv.includes('--check')) {
  assert.equal(fs.readFileSync(path.join(root, destination), 'utf8'), output, 'Regenerate the Category Tables profile');
} else {
  fs.mkdirSync(path.dirname(path.join(root, destination)), { recursive: true });
  fs.writeFileSync(path.join(root, destination), output);
}
