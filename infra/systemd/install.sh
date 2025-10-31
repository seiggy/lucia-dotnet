#!/usr/bin/env bash
# Lucia systemd Service Installation Script
# Copyright (c) 2025 Lucia Project
# License: MIT

set -euo pipefail

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
INSTALL_DIR="/opt/lucia"
CONFIG_DIR="/etc/lucia"
SERVICE_FILE="lucia.service"
ENV_FILE="lucia.env"
SERVICE_PATH="/etc/systemd/system/${SERVICE_FILE}"
DOTNET_VERSION="10.0"
REQUIRED_REDIS_VERSION="7.0"

# Script directory (where this script is located)
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Print colored messages
print_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

print_header() {
    echo ""
    echo -e "${BLUE}═══════════════════════════════════════════════════${NC}"
    echo -e "${BLUE}  Lucia AI Assistant - systemd Installation${NC}"
    echo -e "${BLUE}═══════════════════════════════════════════════════${NC}"
    echo ""
}

# Check if running as root
check_root() {
    if [[ $EUID -ne 0 ]]; then
        print_error "This script must be run as root (use sudo)"
        exit 1
    fi
}

# Detect Linux distribution
detect_distro() {
    if [ -f /etc/os-release ]; then
        . /etc/os-release
        DISTRO=$ID
        VERSION=$VERSION_ID
        print_info "Detected distribution: $NAME $VERSION"
    else
        print_error "Cannot detect Linux distribution"
        exit 1
    fi
}

# Check prerequisites
check_prerequisites() {
    print_info "Checking prerequisites..."
    
    local missing_deps=()
    
    # Check for systemd
    if ! command -v systemctl &> /dev/null; then
        missing_deps+=("systemd")
    fi
    
    # Check for .NET runtime
    if ! command -v dotnet &> /dev/null; then
        missing_deps+=("dotnet-runtime-${DOTNET_VERSION}")
        print_warning ".NET ${DOTNET_VERSION} runtime not found"
    else
        local dotnet_ver=$(dotnet --version)
        print_info "Found .NET version: $dotnet_ver"
        
        # Check if version is 10.x
        if [[ ! $dotnet_ver =~ ^10\. ]]; then
            print_warning ".NET ${DOTNET_VERSION} required, found $dotnet_ver"
            missing_deps+=("dotnet-runtime-${DOTNET_VERSION}")
        fi
    fi
    
    # Check for Redis
    if ! command -v redis-cli &> /dev/null; then
        missing_deps+=("redis-server")
        print_warning "Redis not found (required dependency)"
    else
        local redis_ver=$(redis-cli --version | grep -oP '\d+\.\d+' | head -1)
        print_info "Found Redis version: $redis_ver"
    fi
    
    # Check for curl (used for health checks)
    if ! command -v curl &> /dev/null; then
        missing_deps+=("curl")
    fi
    
    if [ ${#missing_deps[@]} -gt 0 ]; then
        print_error "Missing required dependencies: ${missing_deps[*]}"
        echo ""
        print_info "Install missing dependencies:"
        
        case $DISTRO in
            ubuntu|debian)
                echo "  sudo apt update"
                [ " ${missing_deps[@]} " =~ " dotnet-runtime-${DOTNET_VERSION} " ] && \
                    echo "  wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh"
                    echo "  chmod +x dotnet-install.sh"
                    echo "  ./dotnet-install.sh --channel ${DOTNET_VERSION} --runtime aspnetcore"
                [ " ${missing_deps[@]} " =~ " redis-server " ] && \
                    echo "  sudo apt install redis-server"
                [ " ${missing_deps[@]} " =~ " curl " ] && \
                    echo "  sudo apt install curl"
                ;;
            rhel|centos|fedora)
                [ " ${missing_deps[@]} " =~ " dotnet-runtime-${DOTNET_VERSION} " ] && \
                    echo "  sudo dnf install dotnet-runtime-${DOTNET_VERSION}"
                [ " ${missing_deps[@]} " =~ " redis-server " ] && \
                    echo "  sudo dnf install redis"
                [ " ${missing_deps[@]} " =~ " curl " ] && \
                    echo "  sudo dnf install curl"
                ;;
            *)
                echo "  Please install: ${missing_deps[*]}"
                ;;
        esac
        
        exit 1
    fi
    
    print_success "All prerequisites satisfied"
}

# Create installation directory
create_install_dir() {
    print_info "Creating installation directory: $INSTALL_DIR"
    
    if [ -d "$INSTALL_DIR" ]; then
        print_warning "Installation directory already exists"
        read -p "Overwrite existing installation? (y/N): " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            print_error "Installation cancelled"
            exit 1
        fi
        rm -rf "$INSTALL_DIR"
    fi
    
    mkdir -p "$INSTALL_DIR"
    print_success "Created $INSTALL_DIR"
}

# Copy application binaries
copy_binaries() {
    print_info "Copying application binaries..."
    
    # Determine source directory
    # If running from repo, build directory is in lucia.AgentHost/bin/Release/net10.0/publish
    local source_dir=""
    
    if [ -d "${SCRIPT_DIR}/../../lucia.AgentHost/bin/Release/net10.0/publish" ]; then
        source_dir="${SCRIPT_DIR}/../../lucia.AgentHost/bin/Release/net10.0/publish"
        print_info "Found binaries in: $source_dir"
    else
        print_error "Cannot find published application binaries"
        print_info "Please publish the application first:"
        echo "  cd ${SCRIPT_DIR}/../.."
        echo "  dotnet publish lucia.AgentHost/lucia.AgentHost.csproj -c Release"
        exit 1
    fi
    
    # Copy all files
    cp -r "$source_dir"/* "$INSTALL_DIR/"
    print_success "Copied application binaries"
}

# Create configuration directory
create_config_dir() {
    print_info "Creating configuration directory: $CONFIG_DIR"
    
    mkdir -p "$CONFIG_DIR"
    
    # Copy environment template
    if [ -f "${SCRIPT_DIR}/lucia.env.example" ]; then
        cp "${SCRIPT_DIR}/lucia.env.example" "${CONFIG_DIR}/${ENV_FILE}"
        chmod 600 "${CONFIG_DIR}/${ENV_FILE}"
        print_success "Created configuration file: ${CONFIG_DIR}/${ENV_FILE}"
    else
        print_error "Environment template not found: ${SCRIPT_DIR}/lucia.env.example"
        exit 1
    fi
}

# Install systemd service
install_service() {
    print_info "Installing systemd service..."
    
    if [ -f "${SCRIPT_DIR}/${SERVICE_FILE}" ]; then
        cp "${SCRIPT_DIR}/${SERVICE_FILE}" "$SERVICE_PATH"
        chmod 644 "$SERVICE_PATH"
        print_success "Installed service file: $SERVICE_PATH"
    else
        print_error "Service file not found: ${SCRIPT_DIR}/${SERVICE_FILE}"
        exit 1
    fi
    
    # Reload systemd
    systemctl daemon-reload
    print_success "Reloaded systemd configuration"
}

# Create log directory
create_log_dir() {
    print_info "Creating log directory..."
    mkdir -p /var/log/lucia
    chmod 755 /var/log/lucia
    print_success "Created /var/log/lucia"
}

# Print next steps
print_next_steps() {
    echo ""
    print_success "✅ Lucia installation complete!"
    echo ""
    print_header
    echo -e "${GREEN}Next Steps:${NC}"
    echo ""
    echo "  1. Edit configuration file:"
    echo -e "     ${BLUE}sudo nano ${CONFIG_DIR}/${ENV_FILE}${NC}"
    echo ""
    echo "  2. Configure required variables:"
    echo "     - HomeAssistant__BaseUrl"
    echo "     - HomeAssistant__AccessToken"
    echo "     - OpenAI__ApiKey"
    echo "     - OpenAI__ModelId"
    echo "     - OpenAI__EmbeddingModelId"
    echo ""
    echo "  3. Ensure Redis is running:"
    echo -e "     ${BLUE}sudo systemctl start redis${NC}"
    echo -e "     ${BLUE}sudo systemctl enable redis${NC}"
    echo ""
    echo "  4. Enable Lucia service on boot:"
    echo -e "     ${BLUE}sudo systemctl enable lucia${NC}"
    echo ""
    echo "  5. Start Lucia service:"
    echo -e "     ${BLUE}sudo systemctl start lucia${NC}"
    echo ""
    echo "  6. Check service status:"
    echo -e "     ${BLUE}sudo systemctl status lucia${NC}"
    echo ""
    echo "  7. View logs:"
    echo -e "     ${BLUE}sudo journalctl -u lucia -f${NC}"
    echo ""
    echo -e "${YELLOW}Documentation:${NC}"
    echo "  - Deployment guide: /opt/lucia/README.md"
    echo "  - GitHub: https://github.com/seiggy/lucia-dotnet"
    echo ""
    print_header
}

# Uninstall function (optional)
uninstall() {
    print_info "Uninstalling Lucia..."
    
    # Stop and disable service
    if systemctl is-active --quiet lucia; then
        systemctl stop lucia
        print_success "Stopped lucia service"
    fi
    
    if systemctl is-enabled --quiet lucia; then
        systemctl disable lucia
        print_success "Disabled lucia service"
    fi
    
    # Remove service file
    if [ -f "$SERVICE_PATH" ]; then
        rm "$SERVICE_PATH"
        systemctl daemon-reload
        print_success "Removed service file"
    fi
    
    # Remove installation directory
    if [ -d "$INSTALL_DIR" ]; then
        rm -rf "$INSTALL_DIR"
        print_success "Removed $INSTALL_DIR"
    fi
    
    # Ask about configuration
    read -p "Remove configuration directory ${CONFIG_DIR}? (y/N): " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        rm -rf "$CONFIG_DIR"
        print_success "Removed $CONFIG_DIR"
    fi
    
    # Ask about logs
    read -p "Remove log directory /var/log/lucia? (y/N): " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        rm -rf /var/log/lucia
        print_success "Removed /var/log/lucia"
    fi
    
    print_success "Uninstallation complete"
}

# Main installation workflow
main() {
    # Check for uninstall flag
    if [[ "${1:-}" == "--uninstall" ]]; then
        check_root
        print_header
        uninstall
        exit 0
    fi
    
    print_header
    check_root
    detect_distro
    check_prerequisites
    create_install_dir
    copy_binaries
    create_config_dir
    create_log_dir
    install_service
    print_next_steps
}

# Run main function
main "$@"
