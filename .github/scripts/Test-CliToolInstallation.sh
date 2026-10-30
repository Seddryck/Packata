#!/bin/sh
# Alpine ARM64 SDK images do not include PowerShell. Keep the container probe POSIX-only.
set -eu

version=$1
source_feed=$(cd "$2" && pwd)
sdk=$3
framework=$4
work=$5
expect_unsupported=${6:-0}

fail() { printf '%s\n' "$*" >&2; exit 1; }
[ ! -e "$work" ] || fail "Test directory already exists: $work"
mkdir -p "$work/feed"
work=$(cd "$work" && pwd)
for package in "$source_feed"/*.nupkg; do
    [ -f "$package" ] || fail "No candidate packages found in $source_feed"
    name=$(basename "$package" | tr '[:upper:]' '[:lower:]')
    cp "$package" "$work/feed/$name"
done
install_version=$(printf '%s' "$version" | tr '[:upper:]' '[:lower:]')
printf '{"sdk":{"version":"%s","rollForward":"disable","allowPrerelease":false}}\n' "$sdk" > "$work/global.json"
printf '%s\n' '<configuration><packageSources><clear/><add key="candidate" value="feed"/></packageSources></configuration>' > "$work/NuGet.Config"
export NUGET_PACKAGES="$work/packages"
cd "$work"
[ "$(dotnet --version)" = "$sdk" ] || fail "The expected SDK $sdk was not selected."
if dotnet tool install Packata-cli --version "$install_version" --tool-path "$work/tool" \
    --framework "$framework" --configfile "$work/NuGet.Config" --no-cache > install.log 2>&1; then
    status=0
else
    status=$?
fi
cat install.log
if [ "$expect_unsupported" = 1 ]; then
    [ "$status" -ne 0 ] || fail "SDK $sdk unexpectedly accepted the RID-aware tool."
    grep -Fq 'Format version is higher than supported' install.log || fail "SDK $sdk failed for an unexpected reason."
    printf 'Confirmed SDK %s cannot install the RID-aware pointer package.\n' "$sdk"
    exit 0
fi
[ "$status" -eq 0 ] || fail "Tool installation failed on SDK $sdk."
if ! "$work/tool/packata" --help > help.log 2>&1; then cat help.log; fail 'Installed CLI help failed.'; fi
grep -q USAGE help.log || fail 'Installed CLI help did not contain a usage section.'
if ! "$work/tool/packata" --version > version.log 2>&1; then cat version.log; fail 'Installed CLI version failed.'; fi
grep -Fq "$version" version.log || fail "Installed CLI did not report $version."
printf 'Installed CLI %s passed help/version checks with SDK %s (%s).\n' "$version" "$sdk" "$framework"
