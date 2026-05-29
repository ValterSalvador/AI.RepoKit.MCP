set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
project="$root/src/AiRepoKit.Cli/AiRepoKit.Cli.csproj"
nuget_dir="$root/artifacts/nuget"
publish_root="$root/artifacts/publish"
manifest_path="$root/artifacts/release-manifest.json"
version="${VERSION:-}"
skip_audit="false"
while [ "$#" -gt 0 ]; do
    case "$1" in
        -Version|--version)
            if [ "$#" -lt 2 ] || [ -z "$2" ]; then
                echo "Unable to resolve release version. $1 requires a value." >&2
                exit 1
            fi
            version="$2"
            shift 2
            ;;
        -SkipAudit|--skip-audit)
            skip_audit="true"
            shift
            ;;
        *)
            if [ -z "$version" ]; then
                version="$1"
            fi
            shift
            ;;
    esac
done
if [ -z "$version" ]; then
    version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$project" | head -n 1)"
fi
version="$(printf '%s' "$version" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"
case "$version" in
    v*|V*)
        version="${version#?}"
        ;;
esac
if [ -z "$version" ]; then
    echo "Unable to resolve release version. Provide VERSION, -Version, --version, a positional version, or set <Version> in $project." >&2
    exit 1
fi
echo "Release version: $version"
target_framework="$(sed -n 's:.*<TargetFramework>\(.*\)</TargetFramework>.*:\1:p' "$project" | head -n 1)"

mkdir -p "$nuget_dir" "$publish_root"

dotnet restore "$root"
dotnet build "$root" -c Release
if [ "${skip_audit:-false}" != "true" ]; then
    dotnet run --project "$project" -- audit --repo "$root"
fi
dotnet pack "$project" -c Release -o "$nuget_dir" -p:Version="$version"

publish_target() {
    rid="$1"
    final_name="$2"
    source_name="$3"
    output="$publish_root/$rid"
    mkdir -p "$output"
    dotnet publish "$project" -c Release -r "$rid" --self-contained true -p:Version="$version" /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true -o "$output"
    if [ -f "$output/$source_name" ] && [ "$source_name" != "$final_name" ]; then
        mv -f "$output/$source_name" "$output/$final_name"
    fi
}

publish_target "win-x64" "airepo.exe" "AiRepoKit.Cli.exe"
publish_target "linux-x64" "airepo" "AiRepoKit.Cli"
publish_target "linux-arm64" "airepo" "AiRepoKit.Cli"

artifact_json() {
    file="$1"
    relative="${file#$root/}"
    sha="$(sha256sum "$file" | awk '{print $1}')"
    size="$(stat -c%s "$file")"
    printf '    {\n      "Path": "%s",\n      "Sha256": "%s",\n      "SizeBytes": %s\n    }' "$relative" "$sha" "$size"
}

{
    printf '{\n'
    printf '  "Version": "%s",\n' "$version"
    printf '  "GeneratedAtLocal": "%s",\n' "$(date +%Y-%m-%dT%H:%M:%S%:z)"
    printf '  "TargetFramework": "%s",\n' "$target_framework"
    printf '  "Artifacts": [\n'
    artifact_json "$nuget_dir/AiRepoKit.Cli.$version.nupkg"
    printf ',\n'
    artifact_json "$publish_root/win-x64/airepo.exe"
    printf ',\n'
    artifact_json "$publish_root/linux-x64/airepo"
    printf ',\n'
    artifact_json "$publish_root/linux-arm64/airepo"
    printf '\n  ]\n'
    printf '}\n'
} > "$manifest_path"

echo "Release artifacts generated in artifacts/"
