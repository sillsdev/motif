#!/usr/bin/env bash
set -euo pipefail

: "${RUNTIME_IDENTIFIER:?}"
: "${STAGE_DIRECTORY:?}"
: "${PRODUCT_VERSION:?}"
: "${NEXT_PRODUCT_VERSION:?}"
: "${FEED_DIRECTORY:?}"
: "${INSTALL_DIRECTORY:?}"
: "${INITIAL_PACKAGE_PATH:?}"
: "${WORK_DIRECTORY:?}"
: "${GITHUB_WORKSPACE:?}"

feed_directory=$(cd "$FEED_DIRECTORY" && pwd)
install_directory=$(mkdir -p "$INSTALL_DIRECTORY" && cd "$INSTALL_DIRECTORY" && pwd)
work_directory=$(mkdir -p "$WORK_DIRECTORY" && cd "$WORK_DIRECTORY" && pwd)
export MOTIF_WORKER_ROOT="$work_directory/worker"
export MOTIF_WRITING_SYSTEM_REPOSITORY_PATH="$work_directory/writing-systems"
export PATH="$HOME/.local/bin:$PATH"
if [[ "$RUNTIME_IDENTIFIER" == linux-x64 ]]; then
    export XDG_CONFIG_HOME="$work_directory/xdg-config"
fi
mkdir -p "$MOTIF_WORKER_ROOT" "$MOTIF_WRITING_SYSTEM_REPOSITORY_PATH"

if [[ "$RUNTIME_IDENTIFIER" == linux-x64 ]]; then
    installed_image="$install_directory/Motif.AppImage"
    cp "$INITIAL_PACKAGE_PATH" "$installed_image"
    chmod +x "$installed_image"
    "$installed_image" --appimage-extract-and-run --cli --version >/dev/null
    appimage_extract_directory="$work_directory/appimage-extract"
    mkdir -p "$appimage_extract_directory"
    (
        cd "$appimage_extract_directory"
        "$installed_image" --appimage-extract >/dev/null
    )
    appimage_root="$appimage_extract_directory/squashfs-root"
    app_executable="$installed_image"
    app_arguments=(--appimage-extract-and-run --smoke)
    config_directory="${XDG_CONFIG_HOME:-$HOME/.config}/SIL/Motif"
elif [[ "$RUNTIME_IDENTIFIER" == osx-arm64 ]]; then
    unzip -q "$INITIAL_PACKAGE_PATH" -d "$install_directory"
    app_count=$(find "$install_directory" -maxdepth 1 -type d -name '*.app' | wc -l | tr -d ' ')
    if [[ "$app_count" != 1 ]]; then
        printf 'Expected one .app in %s; found %s.\n' "$install_directory" "$app_count" >&2
        exit 1
    fi
    app_bundle=$(find "$install_directory" -maxdepth 1 -type d -name '*.app' -print -quit)
    app_executable="$app_bundle/Contents/MacOS/SIL.Motif.App"
    config_directory="$HOME/Library/Application Support/SIL/Motif"
    "$app_executable" --cli --version >/dev/null
else
    printf 'Unsupported Unix package RID: %s\n' "$RUNTIME_IDENTIFIER" >&2
    exit 1
fi

if [[ "$RUNTIME_IDENTIFIER" == linux-x64 ]]; then
    worker_search_root="$appimage_root"
else
    worker_search_root="$app_bundle/Contents/MacOS"
fi
worker_executables=$(find "$worker_search_root" -type f -name SIL.Motif.Worker -perm -u+x -print)
worker_executable_count=$(printf '%s\n' "$worker_executables" | sed '/^$/d' | wc -l | tr -d ' ')
if [[ "$worker_executable_count" != 1 ]]; then
    printf 'Expected one packaged Worker under %s; found %s:\n%s\n' \
        "$worker_search_root" "$worker_executable_count" "$worker_executables" >&2
    exit 1
fi
worker_executable=$(printf '%s\n' "$worker_executables" | sed -n '1p')
worker_runtime_config="$worker_executable.runtimeconfig.json"
staged_worker_runtime_config="$STAGE_DIRECTORY/SIL.Motif.Worker.runtimeconfig.json"
if [[ ! -f "$worker_runtime_config" || ! -f "$staged_worker_runtime_config" ]]; then
    printf 'Worker runtimeconfig is missing from the installed package or staging directory.\n' >&2
    exit 1
fi
python3 - "$worker_runtime_config" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as runtime_config_file:
    runtime_config = json.load(runtime_config_file)
included_frameworks = runtime_config.get("runtimeOptions", {}).get("includedFrameworks", [])
if not included_frameworks:
    raise SystemExit(f"Packaged Worker runtimeconfig is not self-contained: {sys.argv[1]}")
PY
if ! cmp -- "$staged_worker_runtime_config" "$worker_runtime_config"; then
    printf 'The installed package changed the Worker runtimeconfig from staging. Staged and packaged files follow.\n' >&2
    cat "$staged_worker_runtime_config" >&2
    cat "$worker_runtime_config" >&2
    exit 1
fi

cli_shim="$HOME/.local/bin/motif"
if [[ ! -x "$cli_shim" ]]; then
    printf 'Motif did not register its installed CLI shim at %s.\n' "$cli_shim" >&2
    exit 1
fi
if [[ ! -f "$config_directory/install.json" ]]; then
    printf 'Motif did not register its discovery record at %s.\n' "$config_directory/install.json" >&2
    exit 1
fi

assert_version() {
    local expected="$1"
    local actual
    actual=$("$cli_shim" --version)
    if [[ "$actual" != "$expected" ]]; then
        printf 'Expected Motif version %s, found %s.\n' "$expected" "$actual" >&2
        exit 1
    fi
}

assert_version "$PRODUCT_VERSION"

sample_builder="$GITHUB_WORKSPACE/bin/Release/SIL.Motif.SampleProjects"
sample_spec="$GITHUB_WORKSPACE/samples/synthetic-turkic/sample.json"
if [[ "$RUNTIME_IDENTIFIER" == linux-x64 ]]; then
    export MOTIF_RELEASE_APP_PREFIX_ARGUMENTS='["--appimage-extract-and-run"]'
    export MOTIF_RELEASE_APP_WRAPPER="$(command -v xvfb-run)"
    export MOTIF_RELEASE_APP_WRAPPER_ARGUMENTS='["-a"]'
fi
pwsh ./tools/release-pathways.ps1 \
    -AppExecutable "$app_executable" \
    -CliExecutable "$cli_shim" \
    -SampleBuilder "$sample_builder" \
    -SampleSpec "$sample_spec" \
    -WalkthroughDirectory "$GITHUB_WORKSPACE/walkthroughs" \
    -WorkDirectory "$work_directory/release-pathway-evidence"
sample_output_root="$work_directory/sample-projects"
sample_build_stderr="$work_directory/sample-project-builder.stderr"
if sample_build_json=$("$sample_builder" build "$sample_spec" "$sample_output_root" 2>"$sample_build_stderr"); then
    project_path=$(printf '%s\n' "$sample_build_json" | python3 -c 'import json,sys; print(json.load(sys.stdin)["projectPath"])')
else
    sample_build_exit=$?
    printf 'Sample project builder failed with exit code %s:\n' "$sample_build_exit" >&2
    cat "$sample_build_stderr" >&2
    exit "$sample_build_exit"
fi
if [[ ! -f "$project_path" ]]; then
    printf 'Sample project builder reported a missing project: %s\n' "$project_path" >&2
    exit 1
fi
"$cli_shim" analyses --project "$project_path" >/dev/null

job_id=$(MOTIF_SUPPRESS_KICK=1 "$cli_shim" baseline-refresh --project "$project_path")
if [[ -z "$job_id" ]]; then
    printf 'Motif did not return a queued Worker job id.\n' >&2
    exit 1
fi
worker_output="$work_directory/worker.log"
"$worker_executable" --root "$MOTIF_WORKER_ROOT" --no-parser --idle-ms 1000 >"$worker_output" 2>&1 &
worker_pid=$!
set +e
wait "$worker_pid"
worker_exit_code=$?
set -e
if [[ "$worker_exit_code" -ne 0 ]]; then
    printf 'Installed Worker exited with code %s; output follows:\n' "$worker_exit_code" >&2
    cat "$worker_output" >&2
    exit 1
fi
job_json=$("$cli_shim" jobs show "$job_id" --project "$project_path" --json)
job_status=$(printf '%s\n' "$job_json" | sed -nE 's/.*"status"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/p')
if [[ "$job_status" != completed ]]; then
    printf 'Installed Worker job %s ended as %s after its Worker exited; Worker output follows:\n%s\n' \
        "$job_id" "$job_status" "$job_json" >&2
    cat "$worker_output" >&2
    exit 1
fi

words_file="$work_directory/words.txt"
printf 'k\n' > "$words_file"
"$cli_shim" assess "$project_path" --words "$words_file" >/dev/null

if [[ "$RUNTIME_IDENTIFIER" == linux-x64 ]]; then
    xvfb-run --auto-servernum "$app_executable" "${app_arguments[@]}"
else
    "$app_executable" --smoke
fi

user_data_directory="$HOME/.local/share/SIL/Motif"
if [[ "$RUNTIME_IDENTIFIER" == osx-arm64 ]]; then
    user_data_directory="$HOME/Library/Application Support/SIL/Motif"
fi
mkdir -p "$user_data_directory"
user_data_marker="$user_data_directory/package-smoke-$RANDOM.txt"
printf 'keep\n' > "$user_data_marker"

"$cli_shim" --update-smoke "$feed_directory" "$RUNTIME_IDENTIFIER" "$NEXT_PRODUCT_VERSION"
updated=false
for _ in $(seq 1 120); do
    if [[ $("$cli_shim" --version 2>/dev/null || true) == "$NEXT_PRODUCT_VERSION" ]]; then
        updated=true
        break
    fi
    sleep 1
done
if [[ "$updated" != true ]]; then
    printf 'Motif did not update from %s to %s.\n' "$PRODUCT_VERSION" "$NEXT_PRODUCT_VERSION" >&2
    exit 1
fi

"$cli_shim" uninstall >/dev/null
if [[ -e "$cli_shim" || -e "$config_directory/install.json" ]]; then
    printf "Motif's CLI shim or discovery record remains after uninstall.\n" >&2
    exit 1
fi
if [[ ! -f "$user_data_marker" ]]; then
    printf 'Motif user data did not survive uninstall.\n' >&2
    exit 1
fi
release_root=$(cat "$work_directory/release-pathway-evidence/release-pathways-root.txt")
python3 - "$release_root/seed-manifest.json" "$project_path" <<'PY'
import json
import os
import sys

with open(sys.argv[1], encoding="utf-8-sig") as manifest_file:
    manifest = json.load(manifest_file)
for group in ("projects", "cliProjects", "extraProjects"):
    for name, project in manifest.get(group, {}).items():
        if not os.path.isfile(project):
            raise SystemExit(f"Seeded FieldWorks project did not survive uninstall: {name} at {project}")
if not os.path.isfile(sys.argv[2]):
    raise SystemExit(f"The package smoke FieldWorks project did not survive uninstall: {sys.argv[2]}")
PY
# A release-check worker can still be finishing a write here, so removal retries before it gives up.
for attempt in 1 2 3 4 5 6 7 8 9 10; do
    rm -rf -- "$release_root" 2>/dev/null && [[ ! -e "$release_root" ]] && break
    sleep 3
done
if [[ -e "$release_root" ]]; then
    printf 'The release check folder could not be removed: %s\n' "$release_root" >&2
    exit 1
fi
rm -f "$user_data_marker"

if [[ "$RUNTIME_IDENTIFIER" == linux-x64 ]]; then
    rm -f "$installed_image"
    if [[ -e "$installed_image" ]]; then
        printf 'The installed AppImage remains after package removal.\n' >&2
        exit 1
    fi
else
    rm -rf -- "$app_bundle"
    if [[ -e "$app_bundle" ]]; then
        printf 'The installed app bundle remains after package removal.\n' >&2
        exit 1
    fi
fi

printf 'Unix package smoke passed for %s: install, read, Worker, PanGloss, app, update, uninstall.\n' \
    "$RUNTIME_IDENTIFIER"
