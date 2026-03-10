#!/bin/sh
# Gluey Installer
# Usage: curl -fsSL https://raw.githubusercontent.com/rebels-software/gluey/main/engine/install.sh | sh
#
# Downloads the latest Gluey binary from GitHub Releases,
# verifies the SHA256 checksum, and installs it.

set -eu

REPO="rebels-software/gluey"
BINARY_NAME="gluey"
GITHUB_API="https://api.github.com"
GITHUB_RELEASES="https://github.com/${REPO}/releases/download"

# --- Helpers ---

has_cmd() {
    command -v "$1" >/dev/null 2>&1
}

is_tty() {
    [ -t 1 ]
}

# Print with optional color when stdout is a terminal
info() {
    if is_tty; then
        printf '\033[1;34m==>\033[0m %s\n' "$1"
    else
        printf '==> %s\n' "$1"
    fi
}

warn() {
    if is_tty; then
        printf '\033[1;33mWarning:\033[0m %s\n' "$1" >&2
    else
        printf 'Warning: %s\n' "$1" >&2
    fi
}

err() {
    if is_tty; then
        printf '\033[1;31mError:\033[0m %s\n' "$1" >&2
    else
        printf 'Error: %s\n' "$1" >&2
    fi
    exit 1
}

# --- Main ---

main() {
    # 1. Check required tools
    if ! has_cmd curl && ! has_cmd wget; then
        err "curl or wget is required but neither was found"
    fi

    if ! has_cmd tar; then
        err "tar is required but was not found"
    fi

    # 2. Detect OS
    OS="$(uname -s)"
    case "$OS" in
        Linux)  os="linux" ;;
        Darwin) os="osx" ;;
        MINGW*|MSYS*|CYGWIN*)
            err "Windows is not supported by this installer. Download the .zip from GitHub Releases instead." ;;
        *)
            err "Unsupported operating system: $OS" ;;
    esac

    # 3. Detect architecture
    ARCH="$(uname -m)"
    case "$ARCH" in
        x86_64|amd64)  arch="x64" ;;
        aarch64|arm64) arch="arm64" ;;
        *)
            err "Unsupported architecture: $ARCH" ;;
    esac

    info "Detected platform: ${os}-${arch}"

    # 4. Create temp directory and set up cleanup
    tmpdir="$(mktemp -d)"
    trap 'rm -rf "$tmpdir"' EXIT INT TERM

    # 5. Get latest release version from GitHub API
    info "Fetching latest release..."
    api_url="${GITHUB_API}/repos/${REPO}/releases/latest"

    if has_cmd curl; then
        response="$(curl -fsSL "$api_url" 2>/dev/null)" || err "Failed to fetch latest release from GitHub API. Check your network connection."
    else
        response="$(wget -qO- "$api_url" 2>/dev/null)" || err "Failed to fetch latest release from GitHub API. Check your network connection."
    fi

    # Extract tag_name without jq (works with grep + sed)
    tag="$(printf '%s' "$response" | grep '"tag_name"' | sed 's/.*"tag_name"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/')"

    if [ -z "$tag" ]; then
        err "Could not determine latest release version"
    fi

    # Strip leading 'v' if present for the version number
    version="$(printf '%s' "$tag" | sed 's/^v//')"

    info "Latest version: ${version} (${tag})"

    # 6. Build asset filenames
    asset_name="${BINARY_NAME}-${version}-${os}-${arch}.tar.gz"
    checksum_name="checksums-${version}.sha256"

    asset_url="${GITHUB_RELEASES}/${tag}/${asset_name}"
    checksum_url="${GITHUB_RELEASES}/${tag}/${checksum_name}"

    # 7. Download the binary archive
    info "Downloading ${asset_name}..."
    if has_cmd curl; then
        curl -fsSL -o "${tmpdir}/${asset_name}" "$asset_url" || err "Failed to download ${asset_name}. Asset may not exist for your platform."
    else
        wget -qO "${tmpdir}/${asset_name}" "$asset_url" || err "Failed to download ${asset_name}. Asset may not exist for your platform."
    fi

    # 8. Download and verify checksum
    info "Verifying checksum..."
    if has_cmd curl; then
        curl -fsSL -o "${tmpdir}/${checksum_name}" "$checksum_url" || err "Failed to download checksum file"
    else
        wget -qO "${tmpdir}/${checksum_name}" "$checksum_url" || err "Failed to download checksum file"
    fi

    # Extract the expected checksum for our asset
    expected_sum="$(grep "${asset_name}" "${tmpdir}/${checksum_name}" | awk '{print $1}')"
    if [ -z "$expected_sum" ]; then
        err "Checksum not found for ${asset_name} in ${checksum_name}"
    fi

    # Compute actual checksum
    if has_cmd sha256sum; then
        actual_sum="$(sha256sum "${tmpdir}/${asset_name}" | awk '{print $1}')"
    elif has_cmd shasum; then
        actual_sum="$(shasum -a 256 "${tmpdir}/${asset_name}" | awk '{print $1}')"
    else
        warn "Neither sha256sum nor shasum found. Skipping checksum verification."
        actual_sum="$expected_sum"
    fi

    if [ "$actual_sum" != "$expected_sum" ]; then
        err "Checksum verification failed.
  Expected: ${expected_sum}
  Actual:   ${actual_sum}
The downloaded file may be corrupted. Please try again."
    fi

    info "Checksum verified"

    # 9. Extract binary
    tar -xzf "${tmpdir}/${asset_name}" -C "$tmpdir"

    # Find the binary (it may be at the root or in a subdirectory)
    extracted_binary=""
    if [ -f "${tmpdir}/${BINARY_NAME}" ]; then
        extracted_binary="${tmpdir}/${BINARY_NAME}"
    elif [ -f "${tmpdir}/Gluey.Cli" ]; then
        extracted_binary="${tmpdir}/Gluey.Cli"
    else
        # Search one level deep
        for f in "${tmpdir}"/*/"${BINARY_NAME}" "${tmpdir}"/*/Gluey.Cli; do
            if [ -f "$f" ]; then
                extracted_binary="$f"
                break
            fi
        done
    fi

    if [ -z "$extracted_binary" ]; then
        err "Could not find ${BINARY_NAME} binary in the downloaded archive"
    fi

    chmod +x "$extracted_binary"

    # 10. Install binary
    install_dir="/usr/local/bin"
    installed=false

    # Try /usr/local/bin first
    if [ -d "$install_dir" ] && [ -w "$install_dir" ]; then
        cp "$extracted_binary" "${install_dir}/${BINARY_NAME}"
        chmod +x "${install_dir}/${BINARY_NAME}"
        installed=true
    elif has_cmd sudo; then
        info "Requesting sudo to install to ${install_dir}..."
        if sudo cp "$extracted_binary" "${install_dir}/${BINARY_NAME}" 2>/dev/null && \
           sudo chmod +x "${install_dir}/${BINARY_NAME}" 2>/dev/null; then
            installed=true
        fi
    fi

    # Fall back to ~/.local/bin
    if [ "$installed" = false ]; then
        install_dir="${HOME}/.local/bin"
        mkdir -p "$install_dir"
        cp "$extracted_binary" "${install_dir}/${BINARY_NAME}"
        chmod +x "${install_dir}/${BINARY_NAME}"
        installed=true

        # Check if ~/.local/bin is in PATH
        case ":${PATH}:" in
            *":${install_dir}:"*) ;;
            *)
                warn "${install_dir} is not in your PATH."
                printf '  Add it by running:\n'
                printf '    export PATH="%s:$PATH"\n' "$install_dir"
                printf '  Or add that line to your shell profile (~/.bashrc, ~/.zshrc, etc.)\n'
                ;;
        esac
    fi

    info "Installed ${BINARY_NAME} to ${install_dir}/${BINARY_NAME}"

    # 11. Print version
    if has_cmd "${install_dir}/${BINARY_NAME}"; then
        installed_version="$("${install_dir}/${BINARY_NAME}" --version 2>/dev/null || true)"
        if [ -n "$installed_version" ]; then
            info "Gluey ${installed_version} is ready to use"
        else
            info "Gluey ${version} is ready to use"
        fi
    else
        info "Gluey ${version} is ready to use"
    fi
}

main "$@"
