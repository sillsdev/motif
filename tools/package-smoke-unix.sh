#!/usr/bin/env bash
set -euo pipefail

: "${RUNTIME_IDENTIFIER:?}"
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

job_id=$("$cli_shim" baseline-refresh --project "$project_path")
if [[ -z "$job_id" ]]; then
    printf 'Motif did not return a queued Worker job id.\n' >&2
    exit 1
fi
job_status=queued
for _ in $(seq 1 120); do
    job_json=$("$cli_shim" jobs show "$job_id" --project "$project_path" --json)
    job_status=$(printf '%s\n' "$job_json" | sed -nE 's/.*"status"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/p')
    if [[ "$job_status" == completed ]]; then
        break
    fi
    if [[ "$job_status" == failed || "$job_status" == cancelled ]]; then
        printf 'Installed Worker job %s ended as %s: %s\n' "$job_id" "$job_status" "$job_json" >&2
        exit 1
    fi
    sleep 1
done
if [[ "$job_status" != completed ]]; then
    printf 'Installed Worker job %s did not complete; last status was %s.\n' "$job_id" "$job_status" >&2
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
