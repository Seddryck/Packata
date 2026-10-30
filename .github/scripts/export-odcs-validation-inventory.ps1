#requires -Version 7.0
# Generate the documentation inventory from schema objects, never instance examples.
$repositoryRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$schemaPath = Join-Path $repositoryRoot 'src/Packata.Application/Validation/Schemas/odcs-v3.2.0.json'
$destination = Join-Path $repositoryRoot 'docs/odcs-schema-assertions.tsv'
$keywords = @('type', 'enum', 'const', 'required', 'pattern', 'format', 'minimum', 'maximum',
    'exclusiveMinimum', 'exclusiveMaximum', 'minItems', 'maxItems', 'uniqueItems', 'minLength',
    'maxLength', 'minProperties', 'maxProperties', 'multipleOf', 'dependentRequired', '$ref')
$schemaMaps = @('properties', 'patternProperties', 'dependentSchemas')
$schemaArrays = @('allOf', 'anyOf', 'oneOf', 'prefixItems')
$schemaMembers = @('items', 'not', 'if', 'then', 'else', 'contains', 'propertyNames',
    'additionalProperties', 'unevaluatedProperties', 'unevaluatedItems', 'additionalItems')
$rows = [System.Collections.Generic.List[string]]::new()
$rows.Add("schemaPath`tkeyword`tconstraint")

function Export-Assertions($node, [string]$path) {
    if ($node -isnot [System.Collections.IDictionary]) { return }
    foreach ($key in $node.Keys) {
        $childPath = $path + '/' + $key.Replace('~', '~0').Replace('/', '~1')
        if ($key -in ($keywords + $schemaMaps + $schemaArrays + $schemaMembers)) {
            $constraint = if ($node[$key] -is [System.Collections.IDictionary] -or $key -in $schemaArrays) {
                '(subschema; see pinned artifact)'
            } else { ConvertTo-Json -InputObject $node[$key] -Depth 64 -Compress }
            $rows.Add("$childPath`t$key`t$constraint")
        }
        Export-Children $node $key $childPath
    }
}

function Export-Children($node, [string]$key, [string]$childPath) {
        if ($key -in ($schemaMaps + @('$defs', 'definitions'))) {
            foreach ($name in $node[$key].Keys) {
                Export-Assertions $node[$key][$name] ($childPath + '/' + $name.Replace('~', '~0').Replace('/', '~1'))
            }
        } elseif ($key -in $schemaArrays) {
            for ($index = 0; $index -lt $node[$key].Count; $index++) {
                Export-Assertions $node[$key][$index] ($childPath + '/' + $index)
            }
        } elseif ($key -in $schemaMembers) { Export-Assertions $node[$key] $childPath }
}

Export-Assertions (Get-Content -LiteralPath $schemaPath -Raw | ConvertFrom-Json -AsHashtable) ''
Set-Content -LiteralPath $destination -Value $rows -Encoding utf8
