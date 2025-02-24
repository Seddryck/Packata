using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Packata.Core.Serialization;
using Packata.Core.Serialization.Yaml;

namespace Packata.Core.Testing.Serialization.Yaml;

public class DataPackageSerializerTests : BaseDataPackageSerializerTests
{
    protected override IDataPackageSerializer GetSerializer()
        => new DataPackageSerializer();

    protected override string GetFormat() => "yaml";

    protected override Stream GetDataPackageProperties()
        => new MemoryStream(Encoding.UTF8.GetBytes(@"
                name: my-data-package
                title: My Data Package
                description: A really long description
                keywords:
                - data
                - example
                homepage: https://github.com/package
                resources:
                - name: data.csv
                  path: https://example.com/data.csv
                  format: csv
            "));

    protected override Stream GetContributorsProperties()
        => new MemoryStream(Encoding.UTF8.GetBytes(@"
                name: my-data-package
                contributors:
                - title: Jane Doe
                  givenName: Jane
                  familyName: Doe
                  organization: The Company
                  email: jane.doe@company.com
                  path: jane-doe.html
                  roles:
                  - creator
                - title: John Doe
                  email: john.doe@company.com
            "));

    protected override Stream GetSourceProperties()
        => new MemoryStream(Encoding.UTF8.GetBytes(@"
                name: my-data-package
                resources:
                - name: data.csv
                  path: https://example.com/data.csv
                  format: csv
                  sources:
                  - title: My article
                    path: https://example.com/article.html
                  - title: My source
                    email: john.doe@company.com
            "));

    protected override Stream GetFieldsProperties()
        => new MemoryStream(Encoding.UTF8.GetBytes(@"
                name: my-data-package
                resources:
                - name: data.csv
                  path: https://example.com/data.csv
                  schema:
                    fieldsMatch: equal
                    fields:
                    - name: field_integer
                      type: integer
                      bareNumber: false
                      groupChar: "",""
                    - name: field_number
                      type: number
                      bareNumber: true
                      groupChar: "" ""
                      decimalChar: "".""
                    - name: field_date
                      type: date
                      format: ""%Y-%m-%d""
                    - name: field_time
                      type: time
                      format: ""%H:%M:%S""
                    - name: field_year
                      type: year
                    - name: field_yearmonth
                      type: yearmonth
                    - name: field_boolean
                      type: boolean
                    - name: field_object
                      type: object
                    ""$schema"": https://datapackage.org/profiles/2.0/tableschema.json
                "));

    protected override Stream GetResourcesProperties()
        => new MemoryStream(Encoding.UTF8.GetBytes(@"
                name: my-data-package
                resources:
                - name: data.csv
                  path: https://example.com/data.csv
                  description: A really long description
                  bytes: 752
                  hash: 2bf9cebe5915601985c8febd3d3d37d1
            "));

    protected override Stream GetResourcesPathProperties()
        => new MemoryStream(Encoding.UTF8.GetBytes(@"
                name: my-data-package
                resources:
                - name: data
                  path:
                  - data_1.csv
                  - data_2.csv
                  format: csv
            "));

    protected override Stream GetMissingValuesAsStringArrayProperties()
        => new MemoryStream(Encoding.UTF8.GetBytes(@"
                name: my-data-package
                resources:
                - name: data.csv
                  path: https://example.com/data.csv
                  schema:
                    fieldsMatch: equal
                    fields:
                    - name: field_integer
                      type: integer
                      bareNumber: false
                      groupChar: "",""
                    missingValues:
                    - ""-""
                    - NaN
                    - ''
                    ""$schema"": https://datapackage.org/profiles/2.0/tableschema.json
            "));

    protected override Stream GetMissingValuesAsObjectsProperties()
        => new MemoryStream(Encoding.UTF8.GetBytes(@"
                name: my-data-package
                resources:
                - name: data.csv
                  path: https://example.com/data.csv
                  schema:
                    fieldsMatch: equal
                    fields:
                    - name: field_integer
                      type: integer
                      bareNumber: false
                      groupChar: "",""
                    missingValues:
                    - value: ""-""
                      label: Missing value
                    - value: NaN
                      label: Not a number
                    - value: ''
                      label: Unknown
                    ""$schema"": https://datapackage.org/profiles/2.0/tableschema.json
            "));

    protected override Stream GetKeysProperties()
        => new MemoryStream(Encoding.UTF8.GetBytes(@"
                name: my-data-package
                resources:
                - name: data.csv
                  path: https://example.com/data.csv
                  schema:
                    fieldsMatch: equal
                    fields:
                    - name: name
                      type: string
                    - name: fk
                      type: integer
                    - name: info
                      type: string
                    - name: parent
                      type: string
                    primaryKey:
                    - name
                    uniqueKeys:
                    - - name
                    - - fk
                      - info
                    foreignKeys:
                    - fields:
                      - fk
                      reference:
                        resource: other.csv
                        fields:
                        - id
                    - fields:
                      - parent
                      reference:
                        fields:
                        - name
                    ""$schema"": https://datapackage.org/profiles/2.0/tableschema.json
        "));
}
