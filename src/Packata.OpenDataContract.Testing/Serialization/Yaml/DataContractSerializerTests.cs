using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Packata.OpenDataContract.Serialization;
using Packata.OpenDataContract.Serialization.Yaml;

namespace Packata.OpenDataContract.Testing.Serialization.Yaml;

public class DataContractSerializerTests : BaseDataContractSerializerTests
{
    protected override IDataContractSerializer GetSerializer()
        => new DataContractSerializer();

    protected override string GetFormat() => "yaml";

    protected override Stream GetDataContractFundamentals()
        => new MemoryStream(Encoding.UTF8.GetBytes(@"
                apiVersion: v3.0.2 # Standard version
                kind: DataContract

                id: 53581432-6c55-4ba2-a65f-72344a91553a
                name: seller_payments_v1
                version: 1.1.0 # Data Contract Version 
                status: active
                domain: seller
                dataProduct: payments
                tenant: ClimateQuantumInc

                authoritativeDefinitions: 
                - url: https://catalog.data.gov/dataset/air-quality 
                  type: businessDefinition
                - url: https://youtu.be/jbY1BKFj9ec
                  type: videoTutorial

                description:
                  purpose: Views built on top of the seller tables.
                  limitations: None
                  usage: Any
                  customProperties:
                  - property: somePropertyName
                    value: property.value
                  - property: dataprocClusterName # Used for specific applications like Elevate
                    value: [cluster name]

                tags: ['finance', 'day-to-day']
            "));

    protected override Stream GetDataContractSchema()
        => new MemoryStream(Encoding.UTF8.GetBytes(@"
                schema:
                  - name: tbl
                    logicalType: object
                    physicalType: table
                    physicalName: tbl_1 
                    description: Provides core payment metrics 
                    authoritativeDefinitions: 
                      - url: https://catalog.data.gov/dataset/air-quality 
                        type: businessDefinition
                      - url: https://youtu.be/jbY1BKFj9ec
                        type: videoTutorial
                    tags: ['finance']
                    dataGranularityDescription: Aggregation on columns txn_ref_dt, pmt_txn_id
                    properties:
                      - name: txn_ref_dt
                        businessName: transaction reference date
                        logicalType: date
                        logicalTypeOptions:
                            format: yyyy-MM-dd
                        physicalType: date
                        description: null
                        partitioned: true
                        partitionKeyPosition: 1
                        criticalDataElement: false
                        tags: []
                        classification: public
                        transformSourceObjects:
                          - table_name_1
                          - table_name_2
                          - table_name_3
                        transformLogic: sel t1.txn_dt as txn_ref_dt from table_name_1 as t1, table_name_2 as t2, table_name_3 as t3 where t1.txn_dt=date-3
                        transformDescription: Defines the logic in business terms. 
                        examples:
                          - 2022-10-03
                          - 2020-01-28
                      - name: rcvr_id
                        primaryKey: true 
                        primaryKeyPosition: 1
                        businessName: receiver id
                        logicalType: string
                        physicalType: varchar(18)
                        required: false
                        description: A description for column rcvr_id.
                        partitioned: false
                        partitionKeyPosition: -1
                        criticalDataElement: false
                        tags: []
                        classification: restricted
                        encryptedName: enc_rcvr_id
                      - name: rcvr_cntry_code
                        primaryKey: false 
                        primaryKeyPosition: -1
                        businessName: receiver country code
                        logicalType: string
                        physicalType: varchar(2)
                        required: false
                        description: null
                        partitioned: false
                        partitionKeyPosition: -1
                        criticalDataElement: false
                        tags: []
                        classification: public
                        authoritativeDefinitions:
                          - url: https://collibra.com/asset/742b358f-71a5-4ab1-bda4-dcdba9418c25
                            type: businessDefinition
                          - url: https://github.com/myorg/myrepo
                            type: transformationImplementation
                          - url: jdbc:postgresql://localhost:5432/adventureworks/tbl_1/rcvr_cntry_code
                            type: implementation
                        encryptedName: rcvr_cntry_code_encrypted
            "));


    protected override Stream GetDataContractSchemaTypedProps()
        => new MemoryStream(Encoding.UTF8.GetBytes(@"
                version: 1.0.0
                kind: DataContract
                id: 53581432-6c55-4ba2-a65f-72344a91553a
                status: active
                name: date_example
                apiVersion: v3.0.2
                schema:
                  - name: tbl_date_example
                    logicalType: object
                    physicalType: table
                    physicalName: tbl_date_example 
                    description: Provides date examples
                    customProperties:
                    - property: somePropertyName
                      value: property.value
                    - property: dataprocClusterName # Used for specific applications like Elevate
                      value: [cluster name]
                    properties:
                        - name: date_field
                          businessName: Date Field
                          logicalType: date
                          logicalTypeOptions:
                            format: yyyy-MM-dd
                          physicalType: date
                          description: A date field example.
                          partitioned: true
                          partitionKeyPosition: 1
                          criticalDataElement: false
                          tags: []
                          classification: public
                          transformSourceObjects:
                          - table_name_1
                          - table_name_2
                          - table_name_3
                          transformLogic: sel t1.date_field as date_field from table_name_1 as t1, table_name_2 as t2, table_name_3 as t3 where t1.date_field=date-3
                          transformDescription: Defines the logic in business terms.
                        - name: integer_field
                          logicalType: integer
                          logicalTypeOptions:
                            format: i64
                            exclusiveMinimum: true
                            minimum: 100
                        - name: number_field
                          logicalType: number
                          logicalTypeOptions:
                            format: f32
                        - name: boolean_field
                          logicalType: boolean
                        - name: string_field
                          logicalType: string
                          logicalTypeOptions:
                            format: email
                            minLength: 10
                            maxLength: 120
            "));

    protected override Stream GetDataContractServers()
        => new MemoryStream(Encoding.UTF8.GetBytes(@"
                version: 1.0.0
                kind: DataContract
                id: 53581432-6c55-4ba2-a65f-72344a91553a
                status: active
                name: date_example
                apiVersion: v3.0.2
                servers:
                  - server: sqlServer001
                    type: sqlserver
                    environment: prod
                    host: 192.80.80.1
                    database: crm
                    schema: dbo
                  - server: duckDb002
                    type: duckdb
                    environment: prod
                    database: ./customers-analysis.duckdb
                    schema: cch
                  - server: azureEU
                    type: azure
                    environment: uat
                    location: az://my-data-location
                    format: parquet
            "));

    //protected override Stream GetContributorsProperties()
    //    => new MemoryStream(Encoding.UTF8.GetBytes(@"
    //            name: my-data-package
    //            contributors:
    //            - title: Jane Doe
    //              givenName: Jane
    //              familyName: Doe
    //              organization: The Company
    //              email: jane.doe@company.com
    //              path: jane-doe.html
    //              roles:
    //              - creator
    //            - title: John Doe
    //              email: john.doe@company.com
    //        "));

    //protected override Stream GetSourceProperties()
    //    => new MemoryStream(Encoding.UTF8.GetBytes(@"
    //            name: my-data-package
    //            resources:
    //            - name: data.csv
    //              path: https://example.com/data.csv
    //              format: csv
    //              sources:
    //              - title: My article
    //                path: https://example.com/article.html
    //              - title: My source
    //                email: john.doe@company.com
    //        "));

    //protected override Stream GetFieldsProperties()
    //    => new MemoryStream(Encoding.UTF8.GetBytes(@"
    //            name: my-data-package
    //            resources:
    //            - name: data.csv
    //              path: https://example.com/data.csv
    //              schema:
    //                fieldsMatch: equal
    //                fields:
    //                - name: field_integer
    //                  type: integer
    //                  bareNumber: false
    //                  groupChar: "",""
    //                - name: field_number
    //                  type: number
    //                  bareNumber: true
    //                  groupChar: "" ""
    //                  decimalChar: "".""
    //                - name: field_date
    //                  type: date
    //                  format: ""%Y-%m-%d""
    //                - name: field_time
    //                  type: time
    //                  format: ""%H:%M:%S""
    //                - name: field_year
    //                  type: year
    //                - name: field_yearmonth
    //                  type: yearmonth
    //                - name: field_boolean
    //                  type: boolean
    //                - name: field_object
    //                  type: object
    //                ""$schema"": https://datapackage.org/profiles/2.0/tableschema.json
    //            "));

    //protected override Stream GetFieldConstraintsProperties()
    //    => new MemoryStream(Encoding.UTF8.GetBytes(@"
    //            name: my-data-package
    //            resources:
    //            - name: data.csv
    //              path: https://example.com/data.csv
    //              schema:
    //                fields:
    //                    - name: field_integer
    //                      type: integer
    //                      constraints:
    //                        required: true
    //                        unique: false
    //                        minimum: 0
    //                        maximum: 100
    //                    - name: field_string
    //                      type: string
    //                      constraints:
    //                        minLength: 3
    //                        maxLength: 5
    //                        pattern: ""^\\d{3}$""
    //        "));

    //protected override Stream GetResourcesProperties()
    //    => new MemoryStream(Encoding.UTF8.GetBytes(@"
    //            name: my-data-package
    //            resources:
    //            - name: data.csv
    //              path: https://example.com/data.csv
    //              description: A really long description
    //              bytes: 752
    //              hash: 2bf9cebe5915601985c8febd3d3d37d1
    //        "));

    //protected override Stream GetResourcesPathProperties()
    //    => new MemoryStream(Encoding.UTF8.GetBytes(@"
    //            name: my-data-package
    //            resources:
    //            - name: data
    //              path:
    //              - data_1.csv
    //              - data_2.csv
    //              format: csv
    //        "));

    //protected override Stream GetMissingValuesAsStringArrayProperties()
    //    => new MemoryStream(Encoding.UTF8.GetBytes(@"
    //            name: my-data-package
    //            resources:
    //            - name: data.csv
    //              path: https://example.com/data.csv
    //              schema:
    //                fieldsMatch: equal
    //                fields:
    //                - name: field_integer
    //                  type: integer
    //                  bareNumber: false
    //                  groupChar: "",""
    //                missingValues:
    //                - ""-""
    //                - NaN
    //                - ''
    //                ""$schema"": https://datapackage.org/profiles/2.0/tableschema.json
    //        "));

    //protected override Stream GetMissingValuesAsObjectsProperties()
    //    => new MemoryStream(Encoding.UTF8.GetBytes(@"
    //            name: my-data-package
    //            resources:
    //            - name: data.csv
    //              path: https://example.com/data.csv
    //              schema:
    //                fieldsMatch: equal
    //                fields:
    //                - name: field_integer
    //                  type: integer
    //                  bareNumber: false
    //                  groupChar: "",""
    //                missingValues:
    //                - value: ""-""
    //                  label: Missing value
    //                - value: NaN
    //                  label: Not a number
    //                - value: ''
    //                  label: Unknown
    //                ""$schema"": https://datapackage.org/profiles/2.0/tableschema.json
    //        "));

    //protected override Stream GetKeysProperties()
    //    => new MemoryStream(Encoding.UTF8.GetBytes(@"
    //            name: my-data-package
    //            resources:
    //            - name: data.csv
    //              path: https://example.com/data.csv
    //              schema:
    //                fieldsMatch: equal
    //                fields:
    //                - name: name
    //                  type: string
    //                - name: fk
    //                  type: integer
    //                - name: info
    //                  type: string
    //                - name: parent
    //                  type: string
    //                primaryKey:
    //                - name
    //                uniqueKeys:
    //                - - name
    //                - - fk
    //                  - info
    //                foreignKeys:
    //                - fields:
    //                  - fk
    //                  reference:
    //                    resource: other.csv
    //                    fields:
    //                    - id
    //                - fields:
    //                  - parent
    //                  reference:
    //                    fields:
    //                    - name
    //                ""$schema"": https://datapackage.org/profiles/2.0/tableschema.json
    //    "));
}
