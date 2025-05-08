
schemathief delta -a .\src\Packata.Core\bin\Release\net8.0\Packata.Core.dll -c Packata.Core.TableDialect -b https://datapackage.org/profiles/2.0/tabledialect.json -x "paths|profile" > .\.schemas\tabledialect.json
schemathief delta -a .\src\Packata.Core\bin\Release\net8.0\Packata.Core.dll -c Packata.Core.Schema -b https://datapackage.org/profiles/2.0/tableschema.json -x "paths|profile" > .\.schemas\tableschema.json
schemathief delta -a .\src\Packata.Core\bin\Release\net8.0\Packata.Core.dll -c Packata.Core.Resource -b https://datapackage.org/profiles/2.0/dataresource.json -x "paths|profile" > .\.schemas\dataresource.json
schemathief delta -a .\src\Packata.Core\bin\Release\net8.0\Packata.Core.dll -c Packata.Core.DataPackage -b https://datapackage.org/profiles/2.0/datapackage.json -x "paths|profile" > .\.schemas\datapackage.json
