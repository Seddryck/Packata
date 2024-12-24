# Function to convert PascalCase to camelCase
function ConvertTo-CamelCase {
    param (
        [string]$PascalCase
    )
    return ($PascalCase.Substring(0, 1).ToLower() + $PascalCase.Substring(1))
}

# Get user input for PascalCase value
$PascalCaseValue = Read-Host "Enter the PascalCase value"

# Generate camelCase value
$CamelCaseValue = ConvertTo-CamelCase -PascalCase $PascalCaseValue

# Use the current directory as the folder path
$FolderPath = Get-Location

# Replace contents in all files and rename files
Get-ChildItem -Path $FolderPath -Recurse | ForEach-Object {
    # Check if it's a file
    if (-not $_.PSIsContainer) {
        # Replace contents in the file
        (Get-Content $_.FullName) -replace '\$TemplateNamePascalCase\$', $PascalCaseValue `
                                   -replace '\$TemplateNameCamelCase\$', $CamelCaseValue |
        Set-Content $_.FullName

        # Rename the file if necessary
        $NewName = $_.Name -replace 'TemplateNamePascalCase', $PascalCaseValue `
                           -replace 'TemplateNameCamelCase', $CamelCaseValue
        if ($NewName -ne $_.Name) {
            Rename-Item -Path $_.FullName -NewName $NewName
        }
    }
}

Get-ChildItem -Path $FolderPath -Recurse | ForEach-Object {
    # Check if it's a folder
    if ($_.PSIsContainer) {
        # Rename the folder if necessary
        $NewName = $_.Name -replace 'TemplateNamePascalCase', $PascalCaseValue `
                           -replace 'TemplateNameCamelCase', $CamelCaseValue
        if ($NewName -ne $_.Name) {
            Rename-Item -Path $_.FullName -NewName $NewName
        }
    }
}
